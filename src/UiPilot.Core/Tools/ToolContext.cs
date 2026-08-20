using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UiPilot.Abstraction;

namespace UiPilot.Tools;

/// <summary>
/// Shared state handed to every tool handler. Provides the UI backend and a helper to marshal
/// work onto the framework UI thread.
/// </summary>
public sealed class ToolContext
{
    private static readonly AsyncLocal<CancellationToken> CallCancellation = new();

    private readonly Func<Func<object?>, object?> _invoke;
    private readonly TimeSpan _uiTimeout;

    internal ToolContext(
        IUiBackend backend,
        Func<Func<object?>, object?> invoke,
        TimeSpan? uiTimeout = null)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        _uiTimeout = uiTimeout ?? TimeSpan.FromSeconds(30);
    }

    public IUiBackend Backend { get; }

    /// <summary>
    /// Cooperative cancellation for the in-flight tool call. Backed by <see cref="AsyncLocal{T}"/>
    /// so concurrent invokes do not share mutable state.
    /// </summary>
    public CancellationToken CancellationToken => CallCancellation.Value;

    /// <summary>Push a per-call cancellation token for the duration of a tool invoke.</summary>
    internal IDisposable PushCancellation(CancellationToken token)
    {
        var previous = CallCancellation.Value;
        CallCancellation.Value = token;
        return new PopCancellation(previous);
    }

    /// <summary>Run <paramref name="func"/> on the UI thread and return its result.</summary>
    public T OnUi<T>(Func<T> func)
    {
        if (func == null) throw new ArgumentNullException(nameof(func));

        var ct = CancellationToken;
        ct.ThrowIfCancellationRequested();

        // Marshal may block forever if the UI thread is wedged; race a deadline so the pipe
        // surface cannot deadlock permanently while holding a client session.
        var invokeTask = Task.Factory.StartNew(
            () => _invoke(() => func()),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);

        try
        {
            if (!invokeTask.Wait(_uiTimeout, ct))
            {
                throw new PilotToolException(
                    PilotErrorCodes.Timeout,
                    $"UI thread invoke timed out after {(int)_uiTimeout.TotalSeconds}s.",
                    "The app UI thread may be blocked. Inspect the target app, then retry.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new PilotToolException(PilotErrorCodes.Canceled, "UI invoke was canceled.");
        }

        if (invokeTask.IsFaulted)
            ExceptionDispatchInfo.Capture(invokeTask.Exception!.GetBaseException()).Throw();

        return (T)invokeTask.Result!;
    }

    /// <summary>Run <paramref name="action"/> on the UI thread.</summary>
    public void OnUi(Action action) => OnUi<object?>(() => { action(); return null; });

    private sealed class PopCancellation : IDisposable
    {
        private readonly CancellationToken _previous;
        private bool _disposed;

        public PopCancellation(CancellationToken previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CallCancellation.Value = _previous;
        }
    }
}

/// <summary>Signature for a pilot tool. Return any object; it is serialized as the RPC result.</summary>
public delegate object? ToolHandler(ToolContext context, System.Text.Json.JsonElement args);
