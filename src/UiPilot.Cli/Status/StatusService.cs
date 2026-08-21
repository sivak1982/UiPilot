using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UiPilot.Cli.Status;

public sealed class StatusService : BackgroundService
{
    private static readonly TimeSpan SessionPollInterval = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly StatusOptions _options;
    private readonly IStatusSnapshotSource _source;
    private readonly OperationHub _hub;
    private readonly ILogger<StatusService> _logger;
    private readonly HttpListener _listener = new();

    public StatusService(
        StatusOptions options,
        IStatusSnapshotSource source,
        OperationHub hub,
        ILogger<StatusService> logger)
    {
        _options = options;
        _source = source;
        _hub = hub;
        _logger = logger;
        _listener.Prefixes.Add(options.Prefix);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        _logger.LogInformation("UiPilot status service listening on {Prefix}", _options.Prefix);

        using var registration = stoppingToken.Register(() =>
        {
            try { _listener.Stop(); } catch (ObjectDisposedException) { }
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (
                stoppingToken.IsCancellationRequested &&
                ex is HttpListenerException or ObjectDisposedException)
            {
                break;
            }

            _ = ProcessContextSafelyAsync(context, stoppingToken);
        }
    }

    public override void Dispose()
    {
        _listener.Close();
        base.Dispose();
    }

    private async Task ProcessContextSafelyAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            await ProcessContextAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryClose(context.Response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Status request failed");
            if (!context.Response.OutputStream.CanWrite)
                return;
            await WriteJsonAsync(context.Response, HttpStatusCode.InternalServerError, new
            {
                error = "internal_error",
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ProcessContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
        if (request.HttpMethod == "GET" && string.Equals(path, "/health", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { status = "ok" }, ct)
                .ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && string.Equals(path, "/v1/status", StringComparison.Ordinal))
        {
            if (!IsBearerAuthorized(request))
            {
                await UnauthorizedAsync(context.Response, ct).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(context.Response, HttpStatusCode.OK, BuildSnapshot(), ct).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && string.Equals(path, "/v1/events", StringComparison.Ordinal))
        {
            if (!request.IsWebSocketRequest)
            {
                await WriteJsonAsync(context.Response, HttpStatusCode.BadRequest, new
                {
                    error = "websocket_required",
                }, ct).ConfigureAwait(false);
                return;
            }
            if (!IsWebSocketAuthorized(request))
            {
                await UnauthorizedAsync(context.Response, ct).ConfigureAwait(false);
                return;
            }

            await StreamEventsAsync(context, ct).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new { error = "not_found" }, ct)
            .ConfigureAwait(false);
    }

    private StatusSnapshotPayload BuildSnapshot()
    {
        var connection = _source.GetSnapshot();
        return new StatusSnapshotPayload
        {
            ActiveSession = connection.ActiveSession,
            Sessions = connection.Sessions,
            Apps = connection.Apps,
            Operations = _hub.Snapshot(),
        };
    }

    private async Task StreamEventsAsync(HttpListenerContext context, CancellationToken ct)
    {
        var accepted = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        using var socket = accepted.WebSocket;
        // Subscribing before the snapshot is built means no transition can slip through the gap
        // between hello and the first streamed update.
        using var subscription = _hub.Subscribe();
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var drain = DrainUntilClosedAsync(socket, closed);
        try
        {
            var snapshot = BuildSnapshot();
            await SendAsync(socket, StatusMessage.Hello(snapshot), closed.Token).ConfigureAwait(false);

            var lastSessions = snapshot.Sessions;
            var lastActiveSession = snapshot.ActiveSession;
            using var pollTimer = new PeriodicTimer(SessionPollInterval);
            var operationReady = subscription.Reader.WaitToReadAsync(closed.Token).AsTask();
            var pollReady = pollTimer.WaitForNextTickAsync(closed.Token).AsTask();

            while (socket.State == WebSocketState.Open)
            {
                var completed = await Task.WhenAny(operationReady, pollReady).ConfigureAwait(false);
                if (completed == operationReady)
                {
                    if (!await operationReady.ConfigureAwait(false))
                        break;

                    while (subscription.Reader.TryRead(out var operationEvent))
                    {
                        await SendAsync(socket, StatusMessage.OperationUpdate(operationEvent), closed.Token)
                            .ConfigureAwait(false);
                    }
                    operationReady = subscription.Reader.WaitToReadAsync(closed.Token).AsTask();
                }

                if (completed == pollReady)
                {
                    if (!await pollReady.ConfigureAwait(false))
                        break;
                    pollReady = pollTimer.WaitForNextTickAsync(closed.Token).AsTask();
                }

                if (socket.State != WebSocketState.Open)
                    break;

                var connection = _source.GetSnapshot();
                var sessions = connection.Sessions;
                var activeSession = connection.ActiveSession;
                if (SessionsChanged(lastSessions, lastActiveSession, sessions, activeSession))
                {
                    lastSessions = sessions;
                    lastActiveSession = activeSession;
                    await SendAsync(socket, StatusMessage.SessionsUpdate(new StatusSessionsPayload
                    {
                        ActiveSession = activeSession,
                        Sessions = sessions,
                    }), closed.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or client disconnect.
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "Status event stream closed by the peer");
        }
        finally
        {
            closed.Cancel();
            await drain.ConfigureAwait(false);
            await TryCloseSocketAsync(socket).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The status stream is server-to-client only, but a close frame still has to be read for the
    /// service to notice a client that went away.
    /// </summary>
    private static async Task DrainUntilClosedAsync(WebSocket socket, CancellationTokenSource closed)
    {
        var buffer = new byte[256];
        try
        {
            while (!closed.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, closed.Token).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch
        {
            // Any receive failure means the peer is gone; cancelling below is the only response.
        }
        finally
        {
            try { closed.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private static bool SessionsChanged(
        IReadOnlyList<StatusSessionInfo> previous,
        string? previousActive,
        IReadOnlyList<StatusSessionInfo> current,
        string? currentActive) =>
        !string.Equals(previousActive, currentActive, StringComparison.Ordinal) ||
        !previous.SequenceEqual(current);

    private static async Task SendAsync(WebSocket socket, StatusMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct)
            .ConfigureAwait(false);
    }

    private static async Task TryCloseSocketAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort courtesy close.
        }
    }

    private bool IsBearerAuthorized(HttpListenerRequest request)
    {
        const string prefix = "Bearer ";
        var authorization = request.Headers["Authorization"];
        return authorization is not null &&
               authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               TokenEquals(authorization[prefix.Length..]);
    }

    private bool IsWebSocketAuthorized(HttpListenerRequest request) =>
        IsBearerAuthorized(request);

    private bool TokenEquals(string? candidate)
    {
        if (candidate is null)
            return false;
        var expectedBytes = Encoding.UTF8.GetBytes(_options.Token);
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        return expectedBytes.Length == candidateBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
    }

    private static Task UnauthorizedAsync(HttpListenerResponse response, CancellationToken ct)
    {
        response.Headers["WWW-Authenticate"] = "Bearer";
        return WriteJsonAsync(response, HttpStatusCode.Unauthorized, new { error = "unauthorized" }, ct);
    }

    private static async Task WriteJsonAsync(
        HttpListenerResponse response,
        HttpStatusCode status,
        object value,
        CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        response.StatusCode = (int)status;
        response.ContentType = "application/json";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload, ct).ConfigureAwait(false);
        response.Close();
    }

    private static void TryClose(HttpListenerResponse response)
    {
        try { response.Close(); } catch { }
    }
}
