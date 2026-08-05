using System.Collections.Generic;
using Avalonia.Logging;

namespace AvaloniaPilot;

/// <summary>
/// Captures Avalonia binding / data warnings via the logging sink into a ring buffer.
/// Avalonia has no PresentationTraceSources equivalent; this is best-effort.
/// </summary>
internal sealed class BindingDiagnostics
{
    private readonly object _gate = new object();
    private readonly Queue<string> _messages = new Queue<string>();
    private readonly int _capacity;
    private Sink? _sink;
    private ILogSink? _previousSink;

    public BindingDiagnostics(int capacity = 500) => _capacity = capacity;

    public void Install()
    {
        if (_sink != null) return;
        _previousSink = Logger.Sink;
        _sink = new Sink(this, _previousSink);
        Logger.Sink = _sink;
    }

    public void Uninstall()
    {
        if (_sink == null) return;
        if (ReferenceEquals(Logger.Sink, _sink))
            Logger.Sink = _previousSink;
        _sink = null;
        _previousSink = null;
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

    private static bool ShouldCapture(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning &&
        (area == "Binding" || area == "Data" || area == "BindingError" || string.IsNullOrEmpty(area));

    private sealed class Sink : ILogSink
    {
        private readonly BindingDiagnostics _owner;
        private readonly ILogSink? _previous;

        public Sink(BindingDiagnostics owner, ILogSink? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public bool IsEnabled(LogEventLevel level, string area) =>
            ShouldCapture(level, area) || (_previous?.IsEnabled(level, area) ?? false);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (_previous?.IsEnabled(level, area) == true)
                _previous.Log(level, area, source, messageTemplate);
            if (ShouldCapture(level, area))
                _owner.Add($"[{area}] {messageTemplate}");
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues)
        {
            if (_previous?.IsEnabled(level, area) == true)
                _previous.Log(level, area, source, messageTemplate, propertyValues);
            if (!ShouldCapture(level, area)) return;

            try
            {
                _owner.Add($"[{area}] {string.Format(messageTemplate, propertyValues)}");
            }
            catch
            {
                _owner.Add($"[{area}] {messageTemplate}");
            }
        }
    }
}
