using System.IO.Pipes;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using UiPilot.Server;

namespace UiPilot.Client.Pipe;

/// <summary>
/// MCP client over a Windows named pipe. Performs the discovery-token auth gate, then speaks
/// standard MCP (<see cref="StreamClientTransport"/>) to the in-app server.
/// </summary>
public sealed class McpPipeClient : IDisposable
{
    private readonly NamedPipeClientStream _stream;
    private readonly McpClient _client;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _disposed;

    private McpPipeClient(NamedPipeClientStream stream, McpClient client)
    {
        _stream = stream;
        _client = client;
    }

    public static async Task<McpPipeClient> ConnectAsync(
        string pipeName,
        string token,
        int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Math.Max(1, timeoutMs));
        var linked = timeoutCts.Token;

        var stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            // timeoutMs covers connect + auth + MCP initialize, not connect alone.
            await stream.ConnectAsync(linked).ConfigureAwait(false);
            await PipeSessionAuth.WriteClientAsync(stream, token, linked).ConfigureAwait(false);
            var client = await McpClient.CreateAsync(
                new StreamClientTransport(stream, stream),
                cancellationToken: linked).ConfigureAwait(false);
            return new McpPipeClient(stream, client);
        }
        catch
        {
            try { stream.Dispose(); } catch { /* ignore */ }
            throw;
        }
    }

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _stream.IsConnected;

    /// <summary>
    /// Invoke an in-app tool (or control method mapped by <see cref="ConnectionManager"/>).
    /// Returns the tool result JSON payload.
    /// </summary>
    public async Task<JsonElement> CallToolAsync(string name, object? args, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var arguments = ToArgumentDictionary(args);
            var result = await _client.CallToolAsync(name, arguments, cancellationToken: ct).ConfigureAwait(false);
            return ExtractResult(result);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<JsonElement> ListToolsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tools = await _client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
            var list = new List<object>();
            foreach (var tool in tools)
                list.Add(new { name = tool.Name, description = tool.Description });
            return JsonSerializer.SerializeToElement(new { tools = list });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _client.PingAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static IReadOnlyDictionary<string, object?>? ToArgumentDictionary(object? args)
    {
        if (args == null) return null;
        if (args is JsonElement el)
        {
            if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return null;
            if (el.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Tool arguments must be a JSON object.", nameof(args));

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in el.EnumerateObject())
                dict[prop.Name] = prop.Value.Clone();
            return dict;
        }

        var serialized = JsonSerializer.SerializeToElement(args);
        return ToArgumentDictionary(serialized);
    }

    private static JsonElement ExtractResult(CallToolResult result)
    {
        var text = ExtractText(result);
        if (result.IsError == true)
        {
            var (code, message, hint) = ParseErrorPayload(text);
            throw new PipeRpcException(0, message, code, hint);
        }

        if (string.IsNullOrWhiteSpace(text) || text == "null")
            return default;

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string ExtractText(CallToolResult result)
    {
        if (result.Content == null || result.Content.Count == 0)
            return "null";

        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text && !string.IsNullOrEmpty(text.Text))
                return text.Text;
        }

        return "null";
    }

    private static (string? Code, string Message, string? Hint) ParseErrorPayload(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? text
                : text;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : "tool_error";
            var hint = root.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String
                ? h.GetString()
                : null;
            return (code, message, hint);
        }
        catch
        {
            return ("tool_error", text, null);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Closing the transport synchronously unblocks active calls. MCP teardown may perform
        // asynchronous protocol work, so never wait for it while ConnectionManager holds its gate.
        try { _stream.Dispose(); } catch { /* ignore */ }
        _ = DisposeClientAsync();
    }

    private async Task DisposeClientAsync()
    {
        try { await _client.DisposeAsync().ConfigureAwait(false); }
        catch { /* disposal is best-effort after the transport closes */ }
    }
}

public sealed class PipeRpcException : Exception
{
    public int RpcCode { get; }
    public string? Code { get; }
    public string? Hint { get; }

    public PipeRpcException(int rpcCode, string message, string? code = null, string? hint = null) : base(message)
    {
        RpcCode = rpcCode;
        Code = code;
        Hint = hint;
    }
}
