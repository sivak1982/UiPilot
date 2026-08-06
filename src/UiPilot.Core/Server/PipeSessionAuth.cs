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
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteClientAsync(Stream stream, string token, CancellationToken ct)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("Token required.", nameof(token));

        await WriteLineAsync(stream, JsonSerializer.Serialize(new { token }), ct).ConfigureAwait(false);
        var response = await ReadLineAsync(stream, ct).ConfigureAwait(false)
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
    public static bool TryAuthenticateServer(Stream stream, string expectedToken, Action<string>? log = null)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrEmpty(expectedToken)) throw new ArgumentException("Token required.", nameof(expectedToken));

        string? line;
        try { line = ReadLine(stream); }
        catch (Exception ex)
        {
            log?.Invoke("UiPilot pipe auth read failed: " + ex.Message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            WriteLine(stream, JsonSerializer.Serialize(new { ok = false, error = "Missing auth line." }));
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var token = doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            if (!string.Equals(token, expectedToken, StringComparison.Ordinal))
            {
                WriteLine(stream, JsonSerializer.Serialize(new { ok = false, error = "Invalid or missing token." }));
                return false;
            }

            WriteLine(stream, """{"ok":true}""");
            return true;
        }
        catch (Exception ex)
        {
            WriteLine(stream, JsonSerializer.Serialize(new { ok = false, error = "Invalid auth JSON: " + ex.Message }));
            return false;
        }
    }

    private static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
    {
        var bytes = Utf8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static void WriteLine(Stream stream, string line)
    {
        var bytes = Utf8.GetBytes(line + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
                return buffer.Length == 0 ? null : Utf8.GetString(buffer.ToArray());
            if (one[0] == (byte)'\n')
                break;
            if (one[0] != (byte)'\r')
                buffer.WriteByte(one[0]);
        }
        return Utf8.GetString(buffer.ToArray());
    }

    private static string? ReadLine(Stream stream)
    {
        var buffer = new MemoryStream();
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
                return buffer.Length == 0 ? null : Utf8.GetString(buffer.ToArray());
            if (b == '\n')
                break;
            if (b != '\r')
                buffer.WriteByte((byte)b);
        }
        return Utf8.GetString(buffer.ToArray());
    }
}
