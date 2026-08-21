using UiPilot.Client;
using UiPilot.Server;

namespace UiPilot.Cli.Status;

/// <summary>
/// Session projection exposed over the status API. Mapping is explicit so transport secrets on
/// <see cref="SessionSnapshot"/> or future additions to it can never reach a status client.
/// </summary>
public sealed record StatusSessionInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required bool IsActive { get; init; }
    public required int Pid { get; init; }
    public required string ProcessName { get; init; }
    public string? MainWindowTitle { get; init; }
    public string? UiFramework { get; init; }
    public required bool LaunchedByCli { get; init; }
    public required bool CanRestart { get; init; }

    public static StatusSessionInfo From(SessionSnapshot session) => new()
    {
        Name = session.Name,
        Kind = session.Kind,
        IsActive = session.IsActive,
        Pid = session.Pid,
        ProcessName = session.ProcessName,
        MainWindowTitle = session.MainWindowTitle,
        UiFramework = session.UiFramework,
        LaunchedByCli = session.LaunchedByCli,
        CanRestart = session.CanRestart,
    };
}

/// <summary>
/// Discovered app projection. <see cref="DiscoveryInfo.Token"/> and
/// <see cref="DiscoveryInfo.PipeName"/> are deliberately not mapped.
/// </summary>
public sealed record StatusAppInfo
{
    public required int Pid { get; init; }
    public required string ProcessName { get; init; }
    public string? MainWindowTitle { get; init; }
    public string? ProtocolVersion { get; init; }
    public string? StartedUtc { get; init; }
    public string? UiFramework { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public static StatusAppInfo From(DiscoveryInfo app) => new()
    {
        Pid = app.Pid,
        ProcessName = app.ProcessName,
        MainWindowTitle = app.MainWindowTitle,
        ProtocolVersion = app.ProtocolVersion,
        StartedUtc = app.StartedUtc,
        UiFramework = app.UiFramework,
        Capabilities = app.Capabilities,
    };
}

public sealed record StatusSnapshotPayload
{
    public string? ActiveSession { get; init; }
    public required IReadOnlyList<StatusSessionInfo> Sessions { get; init; }
    public required IReadOnlyList<StatusAppInfo> Apps { get; init; }
    public required OperationSnapshot Operations { get; init; }
}

public sealed record StatusSessionsPayload
{
    public string? ActiveSession { get; init; }
    public required IReadOnlyList<StatusSessionInfo> Sessions { get; init; }
}

/// <summary>
/// Single envelope for every <c>/v1/events</c> frame. Clients switch on <see cref="Type"/>:
/// <c>hello</c> carries the initial snapshot, <c>operation</c> a single operation transition,
/// and <c>sessions</c> a changed session list.
/// </summary>
public sealed record StatusMessage
{
    public required string Type { get; init; }
    public required DateTimeOffset SentAt { get; init; }
    public StatusSnapshotPayload? Snapshot { get; init; }
    public OperationEvent? Operation { get; init; }
    public StatusSessionsPayload? Sessions { get; init; }

    public static StatusMessage Hello(StatusSnapshotPayload snapshot) => new()
    {
        Type = "hello",
        SentAt = DateTimeOffset.UtcNow,
        Snapshot = snapshot,
    };

    public static StatusMessage OperationUpdate(OperationEvent operation) => new()
    {
        Type = "operation",
        SentAt = DateTimeOffset.UtcNow,
        Operation = operation,
    };

    public static StatusMessage SessionsUpdate(StatusSessionsPayload sessions) => new()
    {
        Type = "sessions",
        SentAt = DateTimeOffset.UtcNow,
        Sessions = sessions,
    };
}

/// <summary>
/// Read-only view of CLI state used to build status snapshots. Abstracted so the status
/// transport can be tested without launching real apps.
/// </summary>
public interface IStatusSnapshotSource
{
    StatusConnectionSnapshot GetSnapshot();
}

public sealed record StatusConnectionSnapshot(
    string? ActiveSession,
    IReadOnlyList<StatusSessionInfo> Sessions,
    IReadOnlyList<StatusAppInfo> Apps);

public sealed class ConnectionManagerSnapshotSource : IStatusSnapshotSource
{
    private readonly ConnectionManager _connection;

    public ConnectionManagerSnapshotSource(ConnectionManager connection) => _connection = connection;

    public StatusConnectionSnapshot GetSnapshot()
    {
        var snapshot = _connection.CaptureSnapshot();
        return new StatusConnectionSnapshot(
            snapshot.ActiveSession,
            snapshot.Sessions.Select(StatusSessionInfo.From).ToArray(),
            snapshot.Apps.Select(StatusAppInfo.From).ToArray());
    }
}
