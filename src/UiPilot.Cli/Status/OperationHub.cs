using System.Threading.Channels;

namespace UiPilot.Cli.Status;

public sealed record OperationEvent
{
    public required string OperationId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string? Session { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public required string Outcome { get; init; }
    public string? ErrorCode { get; init; }
    public string? MessageSummary { get; init; }
}

public sealed record OperationSnapshot(
    IReadOnlyList<OperationEvent> Current,
    IReadOnlyList<OperationEvent> Recent);

public sealed class OperationHub
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Dictionary<string, OperationEvent> _current = new(StringComparer.Ordinal);
    private readonly Queue<OperationEvent> _recent = new();
    private readonly Dictionary<long, Channel<OperationEvent>> _subscribers = new();
    private long _nextSubscriber;

    public OperationHub(int capacity = 100)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public OperationScope Start(string name, string category, string? session = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var started = new OperationEvent
        {
            OperationId = Guid.NewGuid().ToString("N"),
            Name = name,
            Category = category,
            Session = string.IsNullOrWhiteSpace(session) ? null : session,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = "running",
        };

        lock (_gate)
            _current.Add(started.OperationId, started);
        Publish(started);
        return new OperationScope(this, started);
    }

    public OperationSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new OperationSnapshot(
                _current.Values.OrderBy(e => e.StartedAt).ToArray(),
                _recent.ToArray());
        }
    }

    public OperationSubscription Subscribe(int capacity = 100)
    {
        var channel = Channel.CreateBounded<OperationEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        long id;
        lock (_gate)
        {
            id = ++_nextSubscriber;
            _subscribers.Add(id, channel);
        }
        return new OperationSubscription(channel.Reader, () => RemoveSubscriber(id));
    }

    private void Complete(
        OperationEvent started,
        string outcome,
        string? errorCode,
        string? messageSummary)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var completed = started with
        {
            CompletedAt = completedAt,
            DurationMs = Math.Max(0, (long)(completedAt - started.StartedAt).TotalMilliseconds),
            Outcome = outcome,
            ErrorCode = errorCode,
            MessageSummary = messageSummary,
        };

        lock (_gate)
        {
            if (!_current.Remove(started.OperationId))
                return;
            _recent.Enqueue(completed);
            while (_recent.Count > _capacity)
                _recent.Dequeue();
        }
        Publish(completed);
    }

    private void Publish(OperationEvent value)
    {
        Channel<OperationEvent>[] subscribers;
        lock (_gate)
            subscribers = _subscribers.Values.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(value);
    }

    private void RemoveSubscriber(long id)
    {
        Channel<OperationEvent>? channel;
        lock (_gate)
        {
            if (!_subscribers.Remove(id, out channel))
                return;
        }
        channel.Writer.TryComplete();
    }

    public sealed class OperationScope
    {
        private readonly OperationHub _owner;
        private readonly OperationEvent _started;
        private int _completed;

        internal OperationScope(OperationHub owner, OperationEvent started)
        {
            _owner = owner;
            _started = started;
        }

        public void Succeed()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
                _owner.Complete(_started, "succeeded", null, null);
        }

        public void Fail(string? errorCode = null, string? messageSummary = null)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
                _owner.Complete(
                    _started,
                    "failed",
                    string.IsNullOrWhiteSpace(errorCode) ? "operation_failed" : errorCode,
                    string.IsNullOrWhiteSpace(messageSummary) ? "Operation failed." : messageSummary);
        }
    }
}

public sealed class OperationSubscription : IDisposable
{
    private Action? _dispose;

    internal OperationSubscription(ChannelReader<OperationEvent> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<OperationEvent> Reader { get; }

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
