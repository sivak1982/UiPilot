using System;
using System.Threading;
using UiPilot.Abstraction;

namespace UiPilot.Tools;

/// <summary>
/// Shared state handed to every tool handler. Provides the UI backend and a helper to marshal
/// work onto the framework UI thread.
/// </summary>
public sealed class ToolContext
{
    private readonly Func<Func<object?>, object?> _invoke;

    internal ToolContext(IUiBackend backend, Func<Func<object?>, object?> invoke)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
    }

    public IUiBackend Backend { get; }

    /// <summary>
    /// Cooperative cancellation token for long-running tools. Set per MCP <c>tools/call</c>
    /// while the handler runs.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    /// <summary>Run <paramref name="func"/> on the UI thread and return its result.</summary>
    public T OnUi<T>(Func<T> func) => (T)_invoke(() => func())!;

    /// <summary>Run <paramref name="action"/> on the UI thread.</summary>
    public void OnUi(Action action) => _invoke(() => { action(); return null; });
}

/// <summary>Signature for a pilot tool. Return any object; it is serialized as the RPC result.</summary>
public delegate object? ToolHandler(ToolContext context, System.Text.Json.JsonElement args);
