using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
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

    private static readonly SemaphoreSlim PortReservationGate = new(1, 1);

    public static PortReservation ReservePort()
    {
        PortReservationGate.Wait();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return new PortReservation(listener, PortReservationGate);
        }
        catch
        {
            listener.Dispose();
            PortReservationGate.Release();
            throw;
        }
    }

    internal sealed class PortReservation : IDisposable
    {
        private TcpListener? _listener;
        private SemaphoreSlim? _gate;

        public PortReservation(TcpListener listener, SemaphoreSlim gate)
        {
            _listener = listener;
            _gate = gate;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public async Task StartAsync(IHostedService service, CancellationToken ct = default)
        {
            if (_listener == null)
                throw new InvalidOperationException("The port reservation was already released.");
            _listener.Stop();
            _listener = null;
            try
            {
                await service.StartAsync(ct);
                using var client = new HttpClient();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var response = await client.GetAsync(
                            $"http://127.0.0.1:{Port}/health", ct);
                        if (response.IsSuccessStatusCode)
                            break;
                    }
                    catch (HttpRequestException) when (DateTime.UtcNow < deadline)
                    {
                        // BackgroundService may not have entered ExecuteAsync yet.
                    }
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException($"Status service did not listen on port {Port}.");
                    await Task.Delay(10, ct);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _gate, null)?.Release();
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _listener, null)?.Stop();
            Interlocked.Exchange(ref _gate, null)?.Release();
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
