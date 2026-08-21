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

    /// <summary>Creates a client that tracks named application sessions.</summary>
    /// <param name="stopAppsOnDispose">
    /// Whether disposing the client stops every process that it launched.
    /// </param>
    public UiPilotClient(bool stopAppsOnDispose = true)
        : this(new ConnectionManager(), stopAppsOnDispose)
    {
    }

    internal UiPilotClient(ConnectionManager connection, bool stopAppsOnDispose = true)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _stopAppsOnDispose = stopAppsOnDispose;
    }

    /// <summary>Gets the name of the session used when a call omits its session argument.</summary>
    public string? ActiveSessionName => _connection.ActiveSessionName;

    /// <summary>Lists live pilot-enabled applications discovered on this machine.</summary>
    public IReadOnlyList<DiscoveryInfo> ListApps() => _connection.ListAlive();

    /// <summary>Lists the pilot and process sessions currently tracked by this client.</summary>
    public IReadOnlyList<SessionSnapshot> ListSessions() => _connection.ListSessions();

    /// <summary>Makes a tracked session the default for subsequent calls.</summary>
    /// <param name="session">The session name returned by a lifecycle call.</param>
    /// <returns>The selected session snapshot.</returns>
    public SessionSnapshot SelectSession(string session) => _connection.SelectSession(session);

    /// <summary>Attaches to a running pilot-enabled application.</summary>
    /// <param name="pid">Process id to attach to, or <see langword="null"/> to search.</param>
    /// <param name="processName">Process name to search for when no process id is supplied.</param>
    /// <param name="uiFramework">Expected UI framework, such as <c>wpf</c>, <c>avalonia</c>, or <c>winforms</c>.</param>
    /// <param name="session">Stable name to assign to the connection.</param>
    /// <param name="ct">Cancellation token for discovery and connection.</param>
    /// <returns>The attached session.</returns>
    public Task<SessionSnapshot> AttachAsync(
        int? pid = null,
        string? processName = null,
        string? uiFramework = null,
        string? session = null,
        CancellationToken ct = default) =>
        _connection.AttachAsync(pid, processName, uiFramework, session, ct);

    /// <summary>Builds a project and starts its application with the UiPilot startup hook.</summary>
    /// <param name="project">Path to the application project.</param>
    /// <param name="configuration">MSBuild configuration.</param>
    /// <param name="platform">Optional MSBuild platform.</param>
    /// <param name="session">Stable name to assign to the application.</param>
    /// <param name="foreground">Whether to show the application in the foreground.</param>
    /// <param name="ct">Cancellation token for build and startup.</param>
    /// <returns>The started pilot session.</returns>
    public Task<SessionSnapshot> BuildAndStartAsync(
        string project,
        string configuration = "Debug",
        string? platform = null,
        string? session = null,
        bool foreground = false,
        CancellationToken ct = default) =>
        _connection.BuildAndStartAsync(project, configuration, platform, session, foreground, ct);

    /// <summary>Starts an executable and connects to its in-process UiPilot server.</summary>
    /// <param name="path">Path to the executable.</param>
    /// <param name="session">Stable name to assign to the application.</param>
    /// <param name="workingDirectory">Process working directory, or the executable directory by default.</param>
    /// <param name="useStartupHook">Whether to inject UiPilot without modifying the application.</param>
    /// <param name="uiFramework">UI framework to load, or <see langword="null"/> to detect it.</param>
    /// <param name="foreground">Whether to show the application in the foreground.</param>
    /// <param name="ct">Cancellation token for startup and connection.</param>
    /// <returns>The started pilot session.</returns>
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

    /// <summary>Starts and tracks a process that does not expose UiPilot UI commands.</summary>
    /// <param name="path">Path to the executable.</param>
    /// <param name="session">Stable name to assign to the process.</param>
    /// <param name="workingDirectory">Process working directory.</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="showWindow">Whether the process may create a visible window.</param>
    /// <param name="ct">Cancellation token for process startup.</param>
    /// <returns>The tracked process session.</returns>
    public Task<SessionSnapshot> StartProcessAsync(
        string path,
        string? session = null,
        string? workingDirectory = null,
        string? arguments = null,
        bool showWindow = true,
        CancellationToken ct = default) =>
        _connection.StartProcessAsync(path, session, workingDirectory, arguments, showWindow, ct);

    /// <summary>Waits until a text file or matching glob contains a regular-expression match.</summary>
    /// <param name="pathOrGlob">File path or glob pattern to monitor.</param>
    /// <param name="pattern">Regular expression to match.</param>
    /// <param name="timeoutMs">Maximum wait in milliseconds.</param>
    /// <param name="pollMs">Polling interval in milliseconds.</param>
    /// <param name="fromEnd">Whether to ignore content present when waiting begins.</param>
    /// <param name="ct">Cancellation token for the wait.</param>
    /// <returns>Details of the matched file and text.</returns>
    public Task<LogWaitResult> WaitForLogAsync(
        string pathOrGlob,
        string pattern,
        int timeoutMs = 60_000,
        int pollMs = 200,
        bool fromEnd = false,
        CancellationToken ct = default) =>
        _connection.WaitForLogAsync(pathOrGlob, pattern, timeoutMs, pollMs, fromEnd, ct);

    /// <summary>Stops and relaunches an application session using its original settings.</summary>
    /// <param name="session">Session to restart, or the active session when omitted.</param>
    /// <param name="ct">Cancellation token for startup and connection.</param>
    /// <returns>The replacement session.</returns>
    public Task<SessionSnapshot> RestartAsync(
        string? session = null,
        CancellationToken ct = default) =>
        _connection.RestartAsync(session, ct);

    /// <summary>Disconnects from a session without stopping its process.</summary>
    /// <param name="session">Session to detach, or the active session when omitted.</param>
    /// <returns>The removed session, or <see langword="null"/> when none matched.</returns>
    public SessionSnapshot? Detach(string? session = null) => _connection.Detach(session);

    /// <summary>Stops one tracked session and its launched process tree.</summary>
    /// <param name="session">Session to stop, or the active session when omitted.</param>
    /// <returns>The stopped session, or <see langword="null"/> when none matched.</returns>
    public SessionSnapshot? StopApp(string? session = null) => _connection.StopApp(session);

    /// <summary>Stops all tracked sessions and returns their final snapshots.</summary>
    public IReadOnlyList<SessionSnapshot> StopAll() => _connection.StopAll();

    /// <summary>Checks that an application's UiPilot command channel is responsive.</summary>
    public Task<PingResult> PingAsync(string? session = null, CancellationToken ct = default) =>
        SendAsync<PingResult>("ping", new { }, session, ct);

    /// <summary>Lists the commands supported by the selected application's UI adapter.</summary>
    public Task<ToolListResult> DescribeAppToolsAsync(
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ToolListResult>("describe", new { }, session, ct);

    /// <summary>Returns the selected application's top-level windows.</summary>
    public Task<WindowListResult> ListWindowsAsync(
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<WindowListResult>(ToolCatalog.ListWindows, new { }, session, ct);

    /// <summary>Searches the live visual tree and returns one page of matching elements.</summary>
    /// <remarks>
    /// Queries match automation id, name, or text. Prefer stable automation ids and
    /// <paramref name="exact"/> for durable tests. Returned ids are scoped to the producing session.
    /// </remarks>
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

    /// <summary>Polls the live visual tree until an element query has at least one match.</summary>
    /// <remarks>Returned ids are scoped to the producing session.</remarks>
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

    /// <summary>Returns detailed state and optional descendants for an element id.</summary>
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

    /// <summary>
    /// Walks up from <paramref name="id"/> to the nearest ancestor of <paramref name="type"/>.
    /// Use it when a search matches a templated control's label: the label is a nested TextBlock,
    /// while the ancestor carries the enabled state and handles the click.
    /// </summary>
    /// <param name="id">Element id returned by a query in the same session.</param>
    /// <param name="type">Required ancestor type, or <see langword="null"/> for the nearest ancestor.</param>
    /// <param name="maxDepth">Maximum number of parent links to inspect.</param>
    /// <param name="session">Session that produced the element id.</param>
    /// <param name="ct">Cancellation token for the command.</param>
    /// <returns>The matching ancestor.</returns>
    public Task<ElementResult> FindAncestorAsync(
        string id,
        string? type = null,
        int maxDepth = 25,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ElementResult>(
            ToolCatalog.FindAncestor,
            new { id, type, maxDepth },
            session,
            ct);

    /// <summary>Clicks an element using the adapter's preferred synthetic interaction.</summary>
    public Task<InteractionResult> ClickAsync(
        string id,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.Click, new { id }, session, ct);

    /// <summary>Performs a real mouse drag between an element or coordinates.</summary>
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

    /// <summary>Replaces the editable text of an element using the adapter's preferred method.</summary>
    public Task<InteractionResult> TypeTextAsync(
        string id,
        string text,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.TypeText, new { id, text }, session, ct);

    /// <summary>Sends a key chord to an element or to the currently focused control.</summary>
    public Task<InteractionResult> PressKeysAsync(
        string keys,
        string? id = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.PressKeys, new { id, keys }, session, ct);

    /// <summary>Scrolls an element by horizontal and vertical deltas.</summary>
    public Task<InteractionResult> ScrollAsync(
        string id,
        double dx = 0,
        double dy = 0,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.Scroll, new { id, dx, dy }, session, ct);

    /// <summary>Moves keyboard focus to an element.</summary>
    public Task<InteractionResult> FocusAsync(
        string id,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.Focus, new { id }, session, ct);

    /// <summary>Selects an item by text or zero-based index from a selection control.</summary>
    public Task<InteractionResult> SelectItemAsync(
        string id,
        string? text = null,
        int? index = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<InteractionResult>(ToolCatalog.SelectItem, new { id, text, index }, session, ct);

    /// <summary>Invokes the command associated with an element without pointer input.</summary>
    public Task<CommandResult> InvokeCommandAsync(
        string id,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<CommandResult>(ToolCatalog.InvokeCommand, new { id }, session, ct);

    /// <summary>Captures a window or element offscreen and returns PNG data.</summary>
    public Task<ScreenshotResult> ScreenshotAsync(
        string? id = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ScreenshotResult>(ToolCatalog.Screenshot, new { id }, session, ct);

    /// <summary>Sets a top-level window to normal, minimized, or maximized state.</summary>
    public Task<WindowStateResult> SetWindowStateAsync(
        string state,
        string? id = null,
        bool activate = false,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<WindowStateResult>(
            ToolCatalog.SetWindowState, new { id, state, activate }, session, ct);

    /// <summary>Resizes and optionally repositions a top-level window.</summary>
    public Task<ResizeWindowResult> ResizeWindowAsync(
        double width,
        double height,
        string? id = null,
        double? x = null,
        double? y = null,
        bool activate = false,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<ResizeWindowResult>(
            ToolCatalog.ResizeWindow, new { id, width, height, x, y, activate }, session, ct);

    /// <summary>Restores and activates a top-level window for human observation.</summary>
    public Task<WindowStateResult> BringToFrontAsync(
        string? id = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<WindowStateResult>(ToolCatalog.BringToFront, new { id }, session, ct);

    /// <summary>Returns binding diagnostics collected by adapters that support them.</summary>
    public Task<BindingErrorsResult> GetBindingErrorsAsync(
        bool clear = false,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<BindingErrorsResult>(ToolCatalog.GetBindingErrors, new { clear }, session, ct);

    /// <summary>Finds clipping, overlap, and related layout issues in a visual-tree subtree.</summary>
    public Task<LayoutResult> AnalyzeLayoutAsync(
        string? root = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<LayoutResult>(ToolCatalog.AnalyzeLayout, new { root }, session, ct);

    /// <summary>Temporarily highlights an element for human observation.</summary>
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
    /// <typeparam name="T">Product-owned response type.</typeparam>
    /// <param name="method">Registered app-tool method name.</param>
    /// <param name="args">Serializable command arguments.</param>
    /// <param name="session">Target session, or the active session when omitted.</param>
    /// <param name="ct">Cancellation token for the command.</param>
    /// <returns>The deserialized app-tool response.</returns>
    public Task<T> InvokeAppToolAsync<T>(
        string method,
        object? args = null,
        string? session = null,
        CancellationToken ct = default) =>
        SendAsync<T>(method, args ?? new { }, session, ct);

    /// <summary>
    /// Low-level typed call for new UiPilot tools. Prefer the named methods above for built-ins.
    /// </summary>
    /// <typeparam name="T">Expected response type.</typeparam>
    /// <param name="method">Wire command name.</param>
    /// <param name="args">Serializable command arguments.</param>
    /// <param name="session">Target session, or the active session when omitted.</param>
    /// <param name="ct">Cancellation token for the command.</param>
    /// <returns>The deserialized command response.</returns>
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

    /// <summary>Releases connections and, by default, stops processes launched by this client.</summary>
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

    /// <summary>Asynchronously releases this client; disposal itself performs no asynchronous work.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
