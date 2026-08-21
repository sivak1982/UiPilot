using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace UiPilot.Tools;

/// <summary>Holds the set of callable tools (built-in plus any registered custom tools).</summary>
public sealed class ToolRegistry
{
    private sealed class Entry
    {
        public string Name = "";
        public string Description = "";
        public JsonElement InputSchema;
        public ToolHandler Handler = (_, _) => null;
        public long Order;
    }

    private readonly ConcurrentDictionary<string, Entry> _tools =
        new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

    private readonly ToolContext _context;
    private long _nextOrder;
    private int _activeInvocations;
    private readonly ManualResetEventSlim _idle = new(initialState: true);

    internal ToolRegistry(ToolContext context) => _context = context;

    /// <summary>Register or replace a tool.</summary>
    public void Register(string name, string description, ToolHandler handler) =>
        Register(name, description, ToolSchemas.EmptyObject, handler);

    /// <summary>Register or replace a tool with an MCP JSON Schema for <c>tools/list</c>.</summary>
    public void Register(string name, string description, JsonElement inputSchema, ToolHandler handler)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool name required.", nameof(name));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var schema = inputSchema.ValueKind == JsonValueKind.Undefined
            ? ToolSchemas.EmptyObject
            : inputSchema.Clone();
        _tools.AddOrUpdate(
            name,
            _ => new Entry
            {
                Name = name,
                Description = description,
                InputSchema = schema,
                Handler = handler,
                Order = Interlocked.Increment(ref _nextOrder),
            },
            (_, existing) => new Entry
            {
                Name = name,
                Description = description,
                InputSchema = schema,
                Handler = handler,
                Order = existing.Order,
            });
    }

    public bool Contains(string name) => _tools.ContainsKey(name);

    public IReadOnlyCollection<string> Names
    {
        get
        {
            var entries = OrderedEntries();
            var names = new List<string>(entries.Count);
            foreach (var e in entries)
                names.Add(e.Name);
            return names.AsReadOnly();
        }
    }

    /// <summary>Snapshot of registered tools for MCP <c>tools/list</c>.</summary>
    public IReadOnlyList<(string Name, string Description, JsonElement InputSchema)> List()
    {
        var entries = OrderedEntries();
        var list = new List<(string, string, JsonElement)>(entries.Count);
        foreach (var e in entries)
            list.Add((e.Name, e.Description, e.InputSchema.Clone()));
        return list;
    }

    /// <summary>
    /// Invoke a tool by name. Throws <see cref="KeyNotFoundException"/> if unknown.
    /// Cancellation is scoped to this call via <see cref="AsyncLocal{T}"/> — no global serialize gate.
    /// </summary>
    public object? Invoke(string name, JsonElement args, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(name, out var entry))
            throw new KeyNotFoundException("Unknown tool: " + name);

        using (_context.PushCancellation(cancellationToken))
        {
            if (Interlocked.Increment(ref _activeInvocations) == 1)
                _idle.Reset();
            try
            {
                return entry.Handler(_context, args);
            }
            catch (PilotToolException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PilotToolException("tool_error", ex.Message);
            }
            finally
            {
                if (Interlocked.Decrement(ref _activeInvocations) == 0)
                    _idle.Set();
            }
        }
    }

    /// <summary>Wait for handlers already executing to leave the backend.</summary>
    internal bool WaitForIdle(TimeSpan timeout) => _idle.Wait(timeout);

    /// <summary>Machine-readable description of every tool (used by the CLI's <c>describe_app_tools</c>).</summary>
    public object Describe()
    {
        var list = new List<object>();
        foreach (var e in OrderedEntries())
            list.Add(new { name = e.Name, description = e.Description, inputSchema = e.InputSchema });
        return new { tools = list };
    }

    private List<Entry> OrderedEntries()
    {
        var entries = new List<Entry>(_tools.Values);
        entries.Sort((left, right) => left.Order.CompareTo(right.Order));
        return entries;
    }
}
