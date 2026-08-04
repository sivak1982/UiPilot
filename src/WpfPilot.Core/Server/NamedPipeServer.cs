using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using WpfPilot.Tools;

namespace WpfPilot.Server;

/// <summary>
/// Accepts named-pipe connections and dispatches newline-delimited JSON-RPC requests to the
/// tool registry. Several clients may be connected at once (an MCP session alongside ad-hoc
/// scripts); tool invocations are serialised so they never interleave. Auth token required on
/// every request. Runs its accept loop on a dedicated background thread.
/// </summary>
internal sealed class NamedPipeServer
{
    private readonly string _pipeName;
    private readonly string _token;
    private readonly ToolRegistry _registry;
    private readonly Action<string> _log;
    private readonly object _invokeGate = new object();

    private Thread? _thread;
    private volatile bool _running;
    private NamedPipeServerStream? _pendingAccept;

    public NamedPipeServer(string pipeName, string token, ToolRegistry registry, Action<string> log)
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
        _thread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "WpfPilot.Pipe",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        // Unblocks WaitForConnection so the accept loop can exit.
        try { _pendingAccept?.Dispose(); }
        catch { /* ignore */ }
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

                // Hand the connection to its own thread and immediately create the next pipe
                // instance, so a second client never has to wait for the first to disconnect.
                var connection = server;
                server = null;
                var worker = new Thread(() =>
                {
                    try { HandleClient(connection); }
                    catch (Exception ex) { if (_running) _log("WpfPilot pipe client error: " + ex.Message); }
                    finally { try { connection.Dispose(); } catch { /* ignore */ } }
                })
                {
                    IsBackground = true,
                    Name = "WpfPilot.PipeClient",
                };
                worker.Start();
            }
            catch (Exception ex)
            {
                if (_running) _log("WpfPilot pipe error: " + ex.Message);
                try { server?.Dispose(); } catch { /* ignore */ }
                // Back off so a persistent failure cannot spin the CPU.
                if (_running) Thread.Sleep(100);
            }
        }
    }

    private void HandleClient(NamedPipeServerStream server)
    {
        var encoding = new UTF8Encoding(false);
        using var reader = new StreamReader(server, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(server, encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        while (_running && server.IsConnected)
        {
            string? line;
            try { line = reader.ReadLine(); }
            catch { break; }
            if (line == null) break;
            if (line.Length == 0) continue;

            var response = Process(line);
            try { writer.WriteLine(response); }
            catch { break; }
        }
    }

    private string Process(string line)
    {
        if (!RpcRequest.TryParse(line, out var request, out var parseError))
            return Rpc.Error(null, RpcCodes.ParseError, parseError ?? "Parse error.");

        if (!string.Equals(request.Token, _token, StringComparison.Ordinal))
            return Rpc.Error(request.Id, RpcCodes.Unauthorized, "Invalid or missing token.");

        try
        {
            switch (request.Method)
            {
                case "ping":
                    return Rpc.Result(request.Id, new { pong = true });
                case "describe":
                    return Rpc.Result(request.Id, _registry.Describe());
                default:
                    if (!_registry.Contains(request.Method))
                        return Rpc.Error(request.Id, RpcCodes.MethodNotFound, "Unknown method: " + request.Method);
                    object? result;
                    lock (_invokeGate)
                        result = _registry.Invoke(request.Method, request.Params);
                    return Rpc.Result(request.Id, result);
            }
        }
        catch (Exception ex)
        {
            return Rpc.Error(request.Id, RpcCodes.ToolError, ex.Message);
        }
    }
}
