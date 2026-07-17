using System;
using System.Windows.Threading;
using WpfPilot.Inspection;

namespace WpfPilot.Tools;

/// <summary>
/// Shared state handed to every tool handler. Provides the element registry, binding
/// diagnostics, and a helper to marshal work onto the WPF UI thread.
/// </summary>
public sealed class ToolContext
{
    private readonly Dispatcher _dispatcher;

    internal ToolContext(Dispatcher dispatcher, ElementRegistry elements, BindingDiagnostics bindings)
    {
        _dispatcher = dispatcher;
        Elements = elements;
        Bindings = bindings;
    }

    public ElementRegistry Elements { get; }

    public BindingDiagnostics Bindings { get; }

    /// <summary>Run <paramref name="func"/> on the WPF dispatcher thread and return its result.</summary>
    public T OnUi<T>(Func<T> func) => _dispatcher.Invoke(func);

    /// <summary>Run <paramref name="action"/> on the WPF dispatcher thread.</summary>
    public void OnUi(Action action) => _dispatcher.Invoke(action);
}

/// <summary>Signature for a WpfPilot tool. Return any object; it is serialized as the RPC result.</summary>
public delegate object? ToolHandler(ToolContext context, System.Text.Json.JsonElement args);
