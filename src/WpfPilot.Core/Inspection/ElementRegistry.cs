using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace WpfPilot.Inspection;

/// <summary>
/// Assigns stable, weakly-held string handles to live elements so the agent can refer to a
/// node across calls without pinning the visual tree in memory. Framework-agnostic (<c>object</c>).
/// </summary>
public sealed class ElementRegistry
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, WeakReference<object>> _byId =
        new Dictionary<string, WeakReference<object>>(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<object, Holder> _byObject =
        new ConditionalWeakTable<object, Holder>();
    private long _counter;
    private int _addsSincePrune;

    /// <summary>Return the existing handle for <paramref name="obj"/> or assign a new one.</summary>
    public string GetOrAdd(object obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        lock (_gate)
        {
            // GetValue works on netstandard2.0 / net472 (TryGetValue does not).
            string? createdId = null;
            var holder = _byObject.GetValue(obj, _ =>
            {
                createdId = "e" + Interlocked.Increment(ref _counter).ToString();
                return new Holder(createdId);
            });

            if (createdId != null)
            {
                _byId[createdId] = new WeakReference<object>(obj);
                _addsSincePrune++;
                if (_addsSincePrune >= 64)
                {
                    PruneNoLock();
                    _addsSincePrune = 0;
                }
            }

            return holder.Id;
        }
    }

    /// <summary>Resolve a handle back to a live element, or null if it was collected/unknown.</summary>
    public object? Resolve(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_gate)
        {
            if (_byId.TryGetValue(id!, out var weak) && weak.TryGetTarget(out var obj))
                return obj;
            return null;
        }
    }

    /// <summary>Resolve and cast, or null.</summary>
    public T? Resolve<T>(string? id) where T : class => Resolve(id) as T;

    /// <summary>Remove handles whose weak targets have been collected.</summary>
    public void Prune()
    {
        lock (_gate)
            PruneNoLock();
    }

    private void PruneNoLock()
    {
        var dead = new List<string>();
        foreach (var pair in _byId)
        {
            if (!pair.Value.TryGetTarget(out _))
                dead.Add(pair.Key);
        }

        foreach (var id in dead)
            _byId.Remove(id);
    }

    private sealed class Holder
    {
        public Holder(string id) => Id = id;
        public string Id { get; }
    }
}
