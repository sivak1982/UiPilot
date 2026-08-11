using System.Text.Json;
using UiPilot.Client.Process;
using UiPilot.Server;
using UiPilot.Tools;

namespace UiPilot.Client;

/// <summary>
/// Typed C# client for the same lifecycle and in-app commands exposed by UiPilot's MCP server.
/// Use MCP to explore a live UI, then call these methods from deterministic product tests.
/// </summary>
public sealed class UiPilotClient : IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConnectionManager _connection;
    private readonly bool _stopAppsOnDispose;
    private bool _disposed;

    public UiPilotClient(bool stopAppsOnDispose = true)
        : this(new ConnectionManager(), stopAppsOnDispose)
    {
    }

    internal UiPilotClient(ConnectionManager connection, bool stopAppsOnDispose = true)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _stopAppsOnDispose = stopAppsOnDispose;
    }

    public string? ActiveSessionName => _connection.ActiveSessionName;

    public IReadOnlyList<DiscoveryInfo> ListApps() => _connection.ListAlive();

    public IReadOnlyList<SessionSnapshot> ListSessions() => _connection.ListSessions();

    public SessionSnapshot SelectSession(string session) => _connection.SelectSession(session);

    public Task<SessionSnapshot> AttachAsync(
        int? pid = null,
        string? processName = null,
        string? uiFramework = null,
        string? session = null,
        CancellationToken ct = default) =>
        _connection.AttachAsync(pid, processName, uiFramework, session, ct);

    public Task<SessionSnapshot> BuildAndStartAsync(
        string project,
        string configuration = "Debug",
        string? platform = null,
        string? session = null,
        bool foreground = false,
        CancellationToken ct = default) =>
        _connection.BuildAndStartAsync(project, configuration, platform, session, foreground, ct);

    public Task<SessionSnapshot> StartAppAsync(
        string path,
        string? session = null,
        string? workingDirectory = null,
        bool useStartupHook = true,
        string? uiFramework = null,
        bool foreground = false,
        CancellationToken ct = default) =>
        _connection.StartAppAsync(
            path, session, workingDirectory, useStartupHook, uiFramework, foreground, ct);

    public Task<SessionSnapshot> StartProcessAsync(
        string path,
        string? session = null,
        string? workingDirectory = null,
        string? arguments = null,
        bool showWindow = true,
        CancellationToken ct = default) =>
        _connection.StartProcessAsync(path, session, workingDirectory, arguments, showWindow, ct);

    public Task<LogWaitResult> WaitForLogAsync(
        string pathOrGlob,
        string pattern,
        int timeoutMs = 60_000,
        int pollMs = 200,
        bool fromEnd = false,
        CancellationToken ct = default) =>
        _connection.WaitForLogAsync(pathOrGlob, pattern, timeoutMs, pollMs, fromEnd, ct);

    public Task<SessionSnapshot> RestartAsync(
        string? session = null,
        CancellationToken ct = default) =>
        _connection.RestartAsync(session, ct);

    public SessionSnapshot? Detach(string? session = null) => _connection.Detach(session);

    public SessionSnapshot? StopApp(string? session = null) => _connection.StopApp(session);

    public IReadOnlyList<SessionSnapshot> StopAll() => _connection.StopAll();

    public Task<PingResult> PingAsync(string? session = null, CancellationToken ct = default) =>
        SendAsync<PingResult>("ping", new { }, session, ct);

    public Task<ToolListResult> DescribeAppToolsAsync(
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ToolListResult>("describe", new { }, session, ct);

    public Task<WindowListResult> ListWindowsAsync(
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<WindowListResult>(ToolCatalog.ListWindows, new { }, session, ct);

    public Task<ElementPageResult> FindElementsAsync(
        string? query = null,
        int limit = 50,
        int offset = 0,
        string? root = null,
        bool exact = false,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ElementPageResult>(
            ToolCatalog.FindElements, new { query, limit, offset, root, exact }, session, ct);

    public Task<ElementPageResult> WaitForElementAsync(
        string query,
        string? root = null,
        int timeoutMs = 10_000,
        int pollMs = 200,
        bool exact = false,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ElementPageResult>(
            ToolCatalog.WaitForElement,
            new { query, root, timeoutMs, pollMs, exact },
            session,
            ct);

    public Task<ElementResult> InspectElementAsync(
        string id,
        bool includeChildren = false,
        int depth = 1,
        IReadOnlyList<string>? properties = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ElementResult>(
            ToolCatalog.InspectElement,
            new { id, includeChildren, depth, properties },
            session,
            ct);

    public Task<InteractionResult> ClickAsync(
        string id,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.Click, new { id }, session, ct);

    public Task<DragResult> DragAsync(
        string? id = null,
        double? fromX = null,
        double? fromY = null,
        double? grabOffsetX = null,
        double? grabOffsetY = null,
        string? toId = null,
        double? toX = null,
        double? toY = null,
        double? dx = null,
        double? dy = null,
        int steps = 24,
        int stepDelayMs = 12,
        int settleMs = 250,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<DragResult>(
            ToolCatalog.Drag,
            new
            {
                id,
                fromX,
                fromY,
                grabOffsetX,
                grabOffsetY,
                toId,
                toX,
                toY,
                dx,
                dy,
                steps,
                stepDelayMs,
                settleMs,
            },
            session,
            ct);

    public Task<InteractionResult> TypeTextAsync(
        string id,
        string text,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.TypeText, new { id, text }, session, ct);

    public Task<InteractionResult> PressKeysAsync(
        string keys,
        string? id = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.PressKeys, new { id, keys }, session, ct);

    public Task<InteractionResult> ScrollAsync(
        string id,
        double dx = 0,
        double dy = 0,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.Scroll, new { id, dx, dy }, session, ct);

    public Task<InteractionResult> FocusAsync(
        string id,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.Focus, new { id }, session, ct);

    public Task<InteractionResult> SelectItemAsync(
        string id,
        string? text = null,
        int? index = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.SelectItem, new { id, text, index }, session, ct);

    public Task<CommandResult> InvokeCommandAsync(
        string id,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<CommandResult>(ToolCatalog.InvokeCommand, new { id }, session, ct);

    public Task<ScreenshotResult> ScreenshotAsync(
        string? id = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ScreenshotResult>(ToolCatalog.Screenshot, new { id }, session, ct);

    public Task<WindowStateResult> SetWindowStateAsync(
        string state,
        string? id = null,
        bool activate = false,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<WindowStateResult>(
            ToolCatalog.SetWindowState, new { id, state, activate }, session, ct);

    public Task<WindowStateResult> BringToFrontAsync(
        string? id = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<WindowStateResult>(ToolCatalog.BringToFront, new { id }, session, ct);

    public Task<BindingErrorsResult> GetBindingErrorsAsync(
        bool clear = false,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<BindingErrorsResult>(ToolCatalog.GetBindingErrors, new { clear }, session, ct);

    public Task<LayoutResult> AnalyzeLayoutAsync(
        string? root = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<LayoutResult>(ToolCatalog.AnalyzeLayout, new { root }, session, ct);

    public Task<HighlightResult> HighlightElementAsync(
        string id,
        int durationMs = 1500,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<HighlightResult>(
            ToolCatalog.HighlightElement, new { id, durationMs }, session, ct);

    /// <summary>
    /// Calls a registered custom app tool and deserializes its response. This keeps product-specific
    /// commands in the product app/test project rather than adding them to UiPilot.
    /// </summary>
    public Task<T> InvokeAppToolAsync<T>(
        string method,
        object? args = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<T>(method, args ?? new { }, session, ct);

    /// <summary>
    /// Low-level typed call for new UiPilot tools. Prefer the named methods above for built-ins.
    /// </summary>
    public async Task<T> SendAsync<T>(
        string method,
        object? args = null,
        string? session = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var result = await _connection.SendAsync(method, args, session, ct).ConfigureAwait(false);
        if (typeof(T) == typeof(JsonElement))
            return (T)(object)result.Clone();
        if (result.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"UiPilot command '{method}' returned no response.");

        return result.Deserialize<T>(Json)
            ?? throw new InvalidOperationException(
                $"UiPilot command '{method}' returned a response that could not be read as {typeof(T).Name}.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_stopAppsOnDispose)
        {
            try { _connection.StopAll(); } catch { /* teardown must not hide the test result */ }
        }
        _connection.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
