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
    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _pipeName;
    private readonly string _token;
    private readonly ToolRegistry _registry;
    private readonly Action<string> _log;

    private Thread? _thread;
    private volatile bool _running;
    private NamedPipeServerStream? _pendingAccept;
    private CancellationTokenSource? _cts;

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
        _thread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "UiPilot.McpPipe",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _pendingAccept?.Dispose(); } catch { /* ignore */ }
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = PipeIntegrity.CreateServer(_pipeName, _log);
                _pendingAccept = server;
                server.WaitForConnection();
                _pendingAccept = null;

                var connection = server;
                server = null;
                var worker = new Thread(() =>
                {
                    try { HandleClient(connection); }
                    catch (Exception ex) { if (_running) _log("UiPilot MCP pipe client error: " + ex.Message); }
                    finally { try { connection.Dispose(); } catch { /* ignore */ } }
                })
                {
                    IsBackground = true,
                    Name = "UiPilot.McpPipeClient",
                };
                worker.Start();
            }
            catch (Exception ex)
            {
                if (_running) _log("UiPilot MCP pipe error: " + ex.Message);
                try { server?.Dispose(); } catch { /* ignore */ }
                if (_running) Thread.Sleep(100);
            }
        }
    }

    private void HandleClient(NamedPipeServerStream stream)
    {
        if (!PipeSessionAuth.TryAuthenticateServer(stream, _token, _log))
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

        // RunAsync blocks until the client disconnects or the server is stopped.
        McpServer server = McpServer.Create(transport, options);
        try
        {
            server.RunAsync(linked.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!_running)
        {
            // Expected during Stop().
        }
        finally
        {
            try { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* ignore */ }
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
                InputSchema = EmptyObjectSchema.Clone(),
            });
        }

        return ValueTask.FromResult(new ListToolsResult { Tools = tools });
    }

    private ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var name = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return ValueTask.FromResult(ErrorResult(
                PilotErrorCodes.InvalidArgs,
                "Tool name is required.",
                "Pass a registered tool name from tools/list."));
        }

        if (!_registry.Contains(name))
        {
            return ValueTask.FromResult(ErrorResult(
                PilotErrorCodes.NotFound,
                "Unknown tool: " + name,
                "Call tools/list or describe_app_tools to see registered tools."));
        }

        try
        {
            var args = ArgsToElement(request.Params?.Arguments);
            var result = _registry.Invoke(name, args, ct);
            var json = result is JsonElement el
                ? el.GetRawText()
                : JsonSerializer.Serialize(result, JsonOptions);
            return ValueTask.FromResult(new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = json },
                },
            });
        }
        catch (PilotToolException ex)
        {
            return ValueTask.FromResult(ErrorResult(ex.Code, ex.Message, ex.Hint));
        }
        catch (OperationCanceledException)
        {
            return ValueTask.FromResult(ErrorResult(
                PilotErrorCodes.Canceled,
                "Tool invocation was canceled.",
                null));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(ErrorResult("tool_error", ex.Message, null));
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
