using UiPilot.Cli;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class ConnectionManagerTests
{
    [Fact]
    public async Task SendWithoutAttachment_DoesNotAutoAttach()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.SendAsync(ToolCatalog.ListWindows, new { }));

        Assert.Equal(PilotErrorCodes.NotAttached, ex.Code);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public async Task AttachMissingPid_ReturnsNotFoundCode()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.AttachAsync(-1));

        Assert.Equal(PilotErrorCodes.NotFound, ex.Code);
    }

    [Fact]
    public async Task RestartWithoutBuild_ReturnsInvalidArgsCode()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.RestartAsync());

        Assert.Equal(PilotErrorCodes.InvalidArgs, ex.Code);
    }
}
