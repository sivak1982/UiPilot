using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using UiPilot.Cli.Tools;
using Xunit;

namespace UiPilot.Tests;

public sealed class ToolErrorResultTests
{
    [Theory]
    [MemberData(nameof(KnownErrors))]
    public void TryCreate_MapsKnownErrors(Exception exception, string expectedCode)
    {
        Assert.True(ToolErrorResult.TryCreate(exception, out var result));
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content!)).Text;
        using var json = JsonDocument.Parse(text);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(exception.Message, json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void TryCreate_LeavesUnknownErrorsForHostHandling()
    {
        Assert.False(ToolErrorResult.TryCreate(
            new InvalidOperationException("unexpected"), out _));
    }

    public static TheoryData<Exception, string> KnownErrors => new()
    {
        { new TimeoutException("late"), "timeout" },
        { new McpException("protocol failed"), "mcp_error" },
    };
}
