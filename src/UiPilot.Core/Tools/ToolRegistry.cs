using System;
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
        public ToolHandler Handler = (_, _) => null;
    }

    private readonly Dictionary<string, Entry> _tools =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

    private readonly ToolContext _context;

    internal ToolRegistry(ToolContext context) => _context = context;

    /// <summary>Register or replace a tool.</summary>
    public void Register(string name, string description, ToolHandler handler)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool name required.", nameof(name));
        _tools[name] = new Entry { Name = name, Description = description, Handler = handler ?? throw new ArgumentNullException(nameof(handler)) };
    }

    private readonly object _invokeGate = new object();

    public bool Contains(string name) => _tools.ContainsKey(name);

    public IReadOnlyCollection<string> Names
    {
        get
        {
            var names = new List<string>();
            foreach (var e in _tools.Values)
                names.Add(e.Name);
            return names.AsReadOnly();
        }
    }

    /// <summary>Snapshot of registered tools for MCP <c>tools/list</c>.</summary>
    public IReadOnlyList<(string Name, string Description)> List()
    {
        var list = new List<(string, string)>(_tools.Count);
        foreach (var e in _tools.Values)
            list.Add((e.Name, e.Description));
        return list;
    }

    /// <summary>Invoke a tool by name. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    public object? Invoke(string name, JsonElement args, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(name, out var entry))
            throw new KeyNotFoundException("Unknown tool: " + name);

        lock (_invokeGate)
        {
            var previous = _context.CancellationToken;
            if (cancellationToken.CanBeCanceled)
                _context.CancellationToken = cancellationToken;
            try
            {
                return entry.Handler(_context, args);
            }
            finally
            {
                _context.CancellationToken = previous;
            }
        }
    }

    /// <summary>Machine-readable description of every tool (used by the CLI's <c>describe_app_tools</c>).</summary>
    public object Describe()
    {
        var list = new List<object>();
        foreach (var e in _tools.Values)
            list.Add(new { name = e.Name, description = e.Description });
        return new { tools = list };
    }
}
