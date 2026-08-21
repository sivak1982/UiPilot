using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UiPilot.Client;
using UiPilot.Cli.Status;
using Xunit;

namespace UiPilot.Tests.Status;

[Trait("Category", "Integration")]
public sealed class StatusServiceTests
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task HealthIsOpen_StatusRequiresBearerAndReturnsSafeSnapshot()
    {
        using var reservation = StatusTestSupport.ReservePort();
        var port = reservation.Port;
        var hub = new OperationHub();
        hub.Start("list_apps", "lifecycle").Succeed();
        using var manager = new ConnectionManager();
        using var service = CreateService(port, new ConnectionManagerSnapshotSource(manager), hub);
        await reservation.StartAsync(service);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
            using var health = await client.GetAsync("health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal("""{"status":"ok"}""", await health.Content.ReadAsStringAsync());

            using var denied = await client.GetAsync("v1/status");
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
            using var allowed = await client.GetAsync("v1/status");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            using var json = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("sessions").ValueKind);
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("apps").ValueKind);
            Assert.Equal(
                "list_apps",
                json.RootElement.GetProperty("operations").GetProperty("recent")[0].GetProperty("name").GetString());

            StatusTestSupport.AssertNoSensitiveFields(json.RootElement);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WebSocket_SendsHelloSnapshotImmediately()
    {
        using var reservation = StatusTestSupport.ReservePort();
        var port = reservation.Port;
        var hub = new OperationHub();
        hub.Start("list_apps", "lifecycle").Succeed();
        var source = new FakeSnapshotSource();
        source.Set("sim", [Session("sim", isActive: true)], [App(4242, "SampleApp")]);
        using var service = CreateService(port, source, hub);
        await reservation.StartAsync(service);

        try
        {
            using var socket = await ConnectAsync(port);

            // No operation runs after the connect, so a snapshot can only arrive because the
            // service greets the client.
            using var hello = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("hello", hello.RootElement.GetProperty("type").GetString());

            var snapshot = hello.RootElement.GetProperty("snapshot");
            Assert.Equal("sim", snapshot.GetProperty("activeSession").GetString());
            Assert.Equal("sim", snapshot.GetProperty("sessions")[0].GetProperty("name").GetString());
            Assert.Equal(4242, snapshot.GetProperty("apps")[0].GetProperty("pid").GetInt32());
            Assert.Equal(
                "list_apps",
                snapshot.GetProperty("operations").GetProperty("recent")[0].GetProperty("name").GetString());
            StatusTestSupport.AssertNoSensitiveFields(hello.RootElement);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WebSocket_AfterHello_SendsOperationAndSessionUpdates()
    {
        using var reservation = StatusTestSupport.ReservePort();
        var port = reservation.Port;
        var hub = new OperationHub();
        var source = new FakeSnapshotSource();
        using var service = CreateService(port, source, hub);
        await reservation.StartAsync(service);

        try
        {
            using var socket = await ConnectAsync(port);
            using var hello = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("hello", hello.RootElement.GetProperty("type").GetString());
            Assert.Empty(hello.RootElement.GetProperty("snapshot").GetProperty("sessions").EnumerateArray());

            // The session list changes before the operation is reported, so the stream must carry
            // both the operation transition and the resulting session update.
            source.Set("sim", [Session("sim", isActive: true)], [App(4242, "SampleApp")]);
            var operation = hub.Start("start_app", "lifecycle", "sim");

            using var started = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("operation", started.RootElement.GetProperty("type").GetString());
            Assert.Equal("start_app", started.RootElement.GetProperty("operation").GetProperty("name").GetString());
            Assert.Equal("running", started.RootElement.GetProperty("operation").GetProperty("outcome").GetString());

            using var sessions = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("sessions", sessions.RootElement.GetProperty("type").GetString());
            Assert.Equal("sim", sessions.RootElement.GetProperty("sessions").GetProperty("activeSession").GetString());
            Assert.Equal(
                "sim",
                sessions.RootElement.GetProperty("sessions").GetProperty("sessions")[0].GetProperty("name").GetString());

            operation.Succeed();
            using var succeeded = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("operation", succeeded.RootElement.GetProperty("type").GetString());
            Assert.Equal("succeeded", succeeded.RootElement.GetProperty("operation").GetProperty("outcome").GetString());
            Assert.True(succeeded.RootElement.GetProperty("operation").GetProperty("durationMs").GetInt64() >= 0);

            // Sessions are unchanged now, so no second session frame should be queued ahead of the
            // next operation frame.
            hub.Start("click", "forwarding", "sim").Fail("not_attached", "Operation failed.");
            using var failedStart = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("click", failedStart.RootElement.GetProperty("operation").GetProperty("name").GetString());
            using var failed = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("operation", failed.RootElement.GetProperty("type").GetString());
            Assert.Equal("failed", failed.RootElement.GetProperty("operation").GetProperty("outcome").GetString());
            Assert.Equal("not_attached", failed.RootElement.GetProperty("operation").GetProperty("errorCode").GetString());

            StatusTestSupport.AssertNoSensitiveFields(started.RootElement);
            StatusTestSupport.AssertNoSensitiveFields(sessions.RootElement);
            StatusTestSupport.AssertNoSensitiveFields(failed.RootElement);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WebSocket_SendsSessionUpdateWhenAppClosesWithoutOperation()
    {
        using var reservation = StatusTestSupport.ReservePort();
        var port = reservation.Port;
        var source = new FakeSnapshotSource();
        source.Set("sim", [Session("sim", isActive: true)], [App(4242, "SampleApp")]);
        using var service = CreateService(port, source, new OperationHub());
        await reservation.StartAsync(service);

        try
        {
            using var socket = await ConnectAsync(port);
            using var hello = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("hello", hello.RootElement.GetProperty("type").GetString());

            source.Set(null, [], []);

            using var sessions = await StatusTestSupport.ReceiveMessageAsync(socket, ReceiveTimeout);
            Assert.Equal("sessions", sessions.RootElement.GetProperty("type").GetString());
            var payload = sessions.RootElement.GetProperty("sessions");
            Assert.False(payload.TryGetProperty("activeSession", out _));
            Assert.Empty(payload.GetProperty("sessions").EnumerateArray());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WebSocket_WithoutBearerToken_IsRejected()
    {
        using var reservation = StatusTestSupport.ReservePort();
        var port = reservation.Port;
        using var service = CreateService(port, new FakeSnapshotSource(), new OperationHub());
        await reservation.StartAsync(service);

        try
        {
            using var socket = new ClientWebSocket();
            var ex = await Assert.ThrowsAsync<WebSocketException>(() =>
                socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/v1/events"), CancellationToken.None));
            Assert.Contains("401", ex.Message, StringComparison.Ordinal);

            using var queryToken = new ClientWebSocket();
            var queryEx = await Assert.ThrowsAsync<WebSocketException>(() =>
                queryToken.ConnectAsync(
                    new Uri($"ws://127.0.0.1:{port}/v1/events?token=test-token"),
                    CancellationToken.None));
            Assert.Contains("401", queryEx.Message, StringComparison.Ordinal);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<ClientWebSocket> ConnectAsync(int port)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer test-token");
        await socket.ConnectAsync(
            new Uri($"ws://127.0.0.1:{port}/v1/events"),
            CancellationToken.None);
        return socket;
    }

    private static StatusService CreateService(int port, IStatusSnapshotSource source, OperationHub hub) =>
        new(new StatusOptions("test-token", port), source, hub, NullLogger<StatusService>.Instance);

    private static StatusSessionInfo Session(string name, bool isActive) => new()
    {
        Name = name,
        Kind = "pilot",
        IsActive = isActive,
        Pid = 4242,
        ProcessName = "SampleApp",
        MainWindowTitle = "Sample",
        UiFramework = "wpf",
        LaunchedByCli = true,
        CanRestart = true,
    };

    private static StatusAppInfo App(int pid, string processName) => new()
    {
        Pid = pid,
        ProcessName = processName,
        ProtocolVersion = "1.0",
        StartedUtc = DateTime.UtcNow.ToString("o"),
        UiFramework = "wpf",
    };

    private sealed class FakeSnapshotSource : IStatusSnapshotSource
    {
        private readonly object _gate = new();
        private string? _activeSession;
        private IReadOnlyList<StatusSessionInfo> _sessions = [];
        private IReadOnlyList<StatusAppInfo> _apps = [];

        public void Set(
            string? activeSession,
            IReadOnlyList<StatusSessionInfo> sessions,
            IReadOnlyList<StatusAppInfo> apps)
        {
            lock (_gate)
            {
                _activeSession = activeSession;
                _sessions = sessions;
                _apps = apps;
            }
        }

        public StatusConnectionSnapshot GetSnapshot()
        {
            lock (_gate)
                return new StatusConnectionSnapshot(_activeSession, _sessions, _apps);
        }
    }
}
