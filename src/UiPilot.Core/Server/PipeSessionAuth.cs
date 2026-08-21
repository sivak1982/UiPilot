using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UiPilot.Server;

/// <summary>
/// One-line session gate before the MCP stream begins. Reads/writes exact lines without a
/// buffering <see cref="StreamReader"/> so MCP framing on the same duplex pipe is not corrupted.
/// </summary>
internal static class PipeSessionAuth
{
    public const int DefaultTimeoutMs = 5_000;
    public const int MaxLineBytes = 4_096;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteClientAsync(Stream stream, string token, CancellationToken ct)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("Token required.", nameof(token));

        await WriteLineAsync(stream, JsonSerializer.Serialize(new { token }), ct).ConfigureAwait(false);
        var response = await ReadLineAsync(stream, MaxLineBytes, ct).ConfigureAwait(false)
            ?? throw new IOException("Connection closed during pipe auth.");

        using var doc = JsonDocument.Parse(response);
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var message = doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                ? err.GetString()
                : "Pipe authentication failed.";
            throw new UnauthorizedAccessException(message ?? "Pipe authentication failed.");
        }
    }

    /// <summary>
    /// Server-side gate. Returns false when the peer sent a bad/missing token (connection should close).
    /// </summary>
    public static Task<bool> TryAuthenticateServerAsync(
        Stream stream,
        string expectedToken,
        Action<string>? log = null,
        int timeoutMs = DefaultTimeoutMs)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrEmpty(expectedToken)) throw new ArgumentException("Token required.", nameof(expectedToken));
        return AuthenticateServerCoreAsync(stream, expectedToken, log, timeoutMs);
    }

    private static async Task<bool> AuthenticateServerCoreAsync(
        Stream stream,
        string expectedToken,
        Action<string>? log,
        int timeoutMs)
    {
        using var cts = new CancellationTokenSource(Math.Max(1, timeoutMs));
        string? line;
        try
        {
            line = await ReadLineAsync(stream, MaxLineBytes, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            log?.Invoke("UiPilot pipe auth timed out waiting for token line.");
            try { await WriteLineAsync(stream, JsonSerializer.Serialize(new { ok = false, error = "Auth timed out." }), CancellationToken.None).ConfigureAwait(false); }
            catch { /* ignore */ }
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke("UiPilot pipe auth read failed: " + ex.Message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            await WriteLineAsync(stream, JsonSerializer.Serialize(new { ok = false, error = "Missing auth line." }), CancellationToken.None).ConfigureAwait(false);
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var token = doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            if (!FixedTimeEquals(token, expectedToken))
            {
                await WriteLineAsync(stream, JsonSerializer.Serialize(new { ok = false, error = "Invalid or missing token." }), CancellationToken.None).ConfigureAwait(false);
                return false;
            }

            await WriteLineAsync(stream, """{"ok":true}""", CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            await WriteLineAsync(
                stream,
                JsonSerializer.Serialize(new { ok = false, error = "Invalid auth JSON: " + ex.Message }),
                CancellationToken.None).ConfigureAwait(false);
            return false;
        }
    }

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (left == null) return false;
        var a = Utf8.GetBytes(left);
        var b = Utf8.GetBytes(right);
        if (a.Length != b.Length)
        {
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(b, b);
            return false;
        }
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
    {
        var bytes = Utf8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string?> ReadLineAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
                return buffer.Length == 0 ? null : Utf8.GetString(buffer.ToArray());
            if (one[0] == (byte)'\n')
                break;
            if (one[0] != (byte)'\r')
            {
                if (buffer.Length >= maxBytes)
                    throw new IOException($"Auth line exceeded {maxBytes} bytes.");
                buffer.WriteByte(one[0]);
            }
        }
        return Utf8.GetString(buffer.ToArray());
    }
}
