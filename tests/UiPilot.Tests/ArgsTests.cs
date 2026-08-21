using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class ArgsTests
{
    [Theory]
    [InlineData("""{"enabled":true}""", true)]
    [InlineData("""{"enabled":false}""", false)]
    public void GetBool_AcceptsJsonBooleans(string json, bool expected)
    {
        var args = TestSupport.Json(json);

        Assert.Equal(expected, args.GetBool("enabled", !expected));
    }

    [Theory]
    [InlineData("""{}""")]
    public void GetBool_UsesFallbackWhenMissing(string json)
    {
        var args = TestSupport.Json(json);

        Assert.True(args.GetBool("enabled", true));
        Assert.False(args.GetBool("enabled", false));
    }

    [Theory]
    [InlineData("""{"enabled":"true"}""")]
    [InlineData("""{"enabled":"1"}""")]
    [InlineData("""{"enabled":"yes"}""")]
    [InlineData("""{"enabled":2}""")]
    public void GetBool_RejectsNonBooleanValues(string json)
    {
        var args = TestSupport.Json(json);

        var error = Assert.Throws<PilotToolException>(() => args.GetBool("enabled", false));
        Assert.Equal(PilotErrorCodes.InvalidArgs, error.Code);
    }
}
