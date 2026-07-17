using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WpfPilot.Cli.Pipe;

/// <summary>
/// Client end of the WpfPilot named-pipe protocol. Sends one JSON request per line and reads one
/// JSON response per line. Requests are serialized (one in flight at a time).
/// </summary>
public sealed class PipeClient : IDisposable
{
    private readonly NamedPipeClientStream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly string _token;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _id;

    private PipeClient(NamedPipeClientStream stream, string token)
    {
        _stream = stream;
        _token = token;
        var encoding = new UTF8Encoding(false);
        _reader = new StreamReader(stream, encoding, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, encoding, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public static async Task<PipeClient> ConnectAsync(string pipeName, string token, int timeoutMs = 5000, CancellationToken ct = default)
    {
        var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
        return new PipeClient(stream, token);
    }

    public bool IsConnected => _stream.IsConnected;

    /// <summary>Send a request and return the <c>result</c> element. Throws on protocol errors.</summary>
    public async Task<JsonElement> SendAsync(string method, object? args, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var id = Interlocked.Increment(ref _id);
            var request = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["token"] = _token,
                ["params"] = args ?? new { },
            };
            var line = JsonSerializer.Serialize(request);
            await _writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);

            var response = await _reader.ReadLineAsync(ct).ConfigureAwait(false)
                ?? throw new IOException("Connection closed by the app.");

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                var code = error.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                throw new PipeRpcException(code, message ?? "Unknown error");
            }
            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        try { _writer.Dispose(); } catch { /* ignore */ }
        try { _reader.Dispose(); } catch { /* ignore */ }
        try { _stream.Dispose(); } catch { /* ignore */ }
        _lock.Dispose();
    }
}

public sealed class PipeRpcException : Exception
{
    public int Code { get; }
    public PipeRpcException(int code, string message) : base(message) => Code = code;
}
