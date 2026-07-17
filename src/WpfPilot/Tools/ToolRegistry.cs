using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WpfPilot.Tools;

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

    public bool Contains(string name) => _tools.ContainsKey(name);

    /// <summary>Invoke a tool by name. Throws <see cref="KeyNotFoundException"/> if unknown.</summary>
    public object? Invoke(string name, JsonElement args)
    {
        if (!_tools.TryGetValue(name, out var entry))
            throw new KeyNotFoundException("Unknown tool: " + name);
        return entry.Handler(_context, args);
    }

    /// <summary>Machine-readable description of every tool (used by the CLI's <c>describe</c>).</summary>
    public object Describe()
    {
        var list = new List<object>();
        foreach (var e in _tools.Values)
            list.Add(new { name = e.Name, description = e.Description });
        return new { tools = list };
    }
}
