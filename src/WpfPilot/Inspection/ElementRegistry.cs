using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

namespace WpfPilot.Inspection;

/// <summary>
/// Assigns stable, weakly-held string handles to live elements so the agent can refer to a
/// node across calls without us pinning the visual tree in memory.
/// </summary>
public sealed class ElementRegistry
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, WeakReference<DependencyObject>> _byId =
        new Dictionary<string, WeakReference<DependencyObject>>(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<DependencyObject, string> _byObject =
        new ConditionalWeakTable<DependencyObject, string>();
    private long _counter;

    /// <summary>Return the existing handle for <paramref name="obj"/> or assign a new one.</summary>
    public string GetOrAdd(DependencyObject obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        lock (_gate)
        {
            if (_byObject.TryGetValue(obj, out var existing))
                return existing;

            var id = "e" + Interlocked.Increment(ref _counter).ToString();
            _byObject.Add(obj, id);
            _byId[id] = new WeakReference<DependencyObject>(obj);
            return id;
        }
    }

    /// <summary>Resolve a handle back to a live element, or null if it was collected/unknown.</summary>
    public DependencyObject? Resolve(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_gate)
        {
            if (_byId.TryGetValue(id!, out var weak) && weak.TryGetTarget(out var obj))
                return obj;
            return null;
        }
    }
}
