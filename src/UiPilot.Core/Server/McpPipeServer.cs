using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UiPilot.Tools;

namespace UiPilot.Server;

/// <summary>
/// Accepts named-pipe connections, authenticates once with the discovery token, then runs an MCP
/// server session (<see cref="StreamServerTransport"/>) that dispatches tools via
/// <see cref="ToolRegistry"/>.
/// </summary>
internal sealed class McpPipeServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _pipeName;
    private readonly string _token;
    private readonly ToolRegistry _registry;
    private readonly Action<string> _log;

    private Task? _acceptTask;
    private volatile bool _running;
    private NamedPipeServerStream? _pendingAccept;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _sessions = new();
    private readonly object _workersGate = new();

    public McpPipeServer(string pipeName, string token, ToolRegistry registry, Action<string> log)
    {
        _pipeName = pipeName;
        _token = token;
        _registry = registry;
        _log = log;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _running = false;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _pendingAccept?.Dispose(); } catch { /* ignore */ }

        try { _acceptTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* cancelled accept is expected */ }

        Task[] sessions;
        lock (_workersGate)
            sessions = _sessions.ToArray();
        try { Task.WhenAll(sessions).Wait(TimeSpan.FromSeconds(1)); }
        catch { /* best-effort drain */ }

        try { _cts?.Dispose(); } catch { /* ignore */ }
        _cts = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (_running && !ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = PipeIntegrity.CreateServer(_pipeName, _log);
                _pendingAccept = server;
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _pendingAccept = null;

                var connection = server;
                server = null;
                var session = HandleClientAsync(connection);
                lock (_workersGate)
                    _sessions.Add(session);
                _ = session.ContinueWith(
                    _ =>
                    {
                        lock (_workersGate)
                            _sessions.Remove(session);
                    },
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (!_running || ct.IsCancellationRequested)
            {
                try { server?.Dispose(); } catch { /* ignore */ }
                break;
            }
            catch (Exception ex)
            {
                if (_running) _log("UiPilot MCP pipe error: " + ex.Message);
                try { server?.Dispose(); } catch { /* ignore */ }
                if (_running)
                    await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream)
    {
        try
        {
            if (!await PipeSessionAuth.TryAuthenticateServerAsync(stream, _token, _log).ConfigureAwait(false))
            {
                _log("UiPilot rejected pipe client (auth failed).");
                return;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            var transport = new StreamServerTransport(stream, stream, serverName: "UiPilot");
            var options = new McpServerOptions
            {
                ServerInfo = new Implementation
                {
                    Name = "UiPilot",
                    Version = Hosting.PilotRuntime.ProtocolVersion,
                },
                Handlers = new McpServerHandlers
                {
                    ListToolsHandler = ListToolsAsync,
                    CallToolHandler = CallToolAsync,
                },
            };

            McpServer server = McpServer.Create(transport, options);
            try
            {
                await server.RunAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!_running)
            {
                // Expected during Stop().
            }
            finally
            {
                try { await server.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            if (_running) _log("UiPilot MCP pipe client error: " + ex.Message);
        }
        finally
        {
            try { stream.Dispose(); } catch { /* ignore */ }
        }
    }

    private ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var tools = new List<Tool>();
        foreach (var entry in _registry.List())
        {
            tools.Add(new Tool
            {
                Name = entry.Name,
                Description = entry.Description,
                InputSchema = entry.InputSchema.Clone(),
            });
        }

        return ValueTask.FromResult(new ListToolsResult { Tools = tools });
    }

    private async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var name = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return ErrorResult(
                PilotErrorCodes.InvalidArgs,
                "Tool name is required.",
                "Pass a registered tool name from tools/list.");
        }

        if (!_registry.Contains(name))
        {
            return ErrorResult(
                PilotErrorCodes.NotFound,
                "Unknown tool: " + name,
                "Call tools/list or describe_app_tools to see registered tools.");
        }

        try
        {
            var args = ArgsToElement(request.Params?.Arguments);
            var result = await Task.Run(() => _registry.Invoke(name, args, ct), ct).ConfigureAwait(false);
            var json = result is JsonElement el
                ? el.GetRawText()
                : JsonSerializer.Serialize(result, JsonOptions);
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = json },
                },
            };
        }
        catch (PilotToolException ex)
        {
            return ErrorResult(ex.Code, ex.Message, ex.Hint);
        }
        catch (OperationCanceledException)
        {
            return ErrorResult(
                PilotErrorCodes.Canceled,
                "Tool invocation was canceled.",
                null);
        }
        catch (Exception ex)
        {
            return ErrorResult("tool_error", ex.Message, null);
        }
    }

    private static JsonElement ArgsToElement(IDictionary<string, JsonElement>? args)
    {
        if (args == null || args.Count == 0)
            return JsonSerializer.SerializeToElement(new { });
        return JsonSerializer.SerializeToElement(args);
    }

    private static CallToolResult ErrorResult(string code, string message, string? hint) => new()
    {
        IsError = true,
        Content = new List<ContentBlock>
        {
            new TextContentBlock
            {
                Text = JsonSerializer.Serialize(new { error = true, code, message, hint }, JsonOptions),
            },
        },
    };
}
