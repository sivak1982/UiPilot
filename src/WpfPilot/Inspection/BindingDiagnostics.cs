using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Diagnostics;

namespace WpfPilot.Inspection;

/// <summary>
/// Captures WPF data-binding trace output (the "BindingExpression path error..." messages that
/// normally only appear in the VS Output window) into a bounded in-memory ring buffer.
/// </summary>
public sealed class BindingDiagnostics
{
    private readonly object _gate = new object();
    private readonly Queue<string> _messages = new Queue<string>();
    private readonly int _capacity;
    private Listener? _listener;

    public BindingDiagnostics(int capacity = 500) => _capacity = capacity;

    public void Install()
    {
        if (_listener != null) return;
        PresentationTraceSources.Refresh();
        _listener = new Listener(this);
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(_listener);
        source.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
    }

    public void Uninstall()
    {
        if (_listener == null) return;
        PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener);
        _listener = null;
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
            return new List<string>(_messages);
    }

    public void Clear()
    {
        lock (_gate)
            _messages.Clear();
    }

    private void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_gate)
        {
            _messages.Enqueue(message.Trim());
            while (_messages.Count > _capacity)
                _messages.Dequeue();
        }
    }

    private sealed class Listener : TraceListener
    {
        private readonly BindingDiagnostics _owner;
        private readonly StringBuilder _pending = new StringBuilder();

        public Listener(BindingDiagnostics owner) => _owner = owner;

        public override void Write(string? message) => _pending.Append(message);

        public override void WriteLine(string? message)
        {
            _pending.Append(message);
            _owner.Add(_pending.ToString());
            _pending.Clear();
        }
    }
}
