using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace UiPilot.Tests.Status;

internal static class StatusTestSupport
{
    /// <summary>
    /// Field names that would leak arguments, typed text, transport secrets, or screenshot bytes
    /// if the status projections ever started serializing them.
    /// </summary>
    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "pipe", "pipeName", "arguments", "args", "parametersJson",
        "text", "keys", "base64", "screenshot", "password", "secret", "path",
    };

    public static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public static async Task<JsonDocument> ReceiveMessageAsync(WebSocket socket, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[8192];
        var text = new StringBuilder();
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, cts.Token);
            Assert.NotEqual(WebSocketMessageType.Close, received.MessageType);
            text.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
            if (received.EndOfMessage)
                return JsonDocument.Parse(text.ToString());
        }
    }

    public static void AssertNoSensitiveFields(JsonElement element)
    {
        foreach (var name in CollectPropertyNames(element))
            Assert.DoesNotContain(name, ForbiddenFields);
    }

    private static IEnumerable<string> CollectPropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in CollectPropertyNames(property.Value))
                        yield return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                foreach (var nested in CollectPropertyNames(item))
                    yield return nested;
                break;
        }
    }
}
