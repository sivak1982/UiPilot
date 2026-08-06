using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class ArgsTests
{
    [Theory]
    [InlineData("""{"enabled":true}""", true)]
    [InlineData("""{"enabled":false}""", false)]
    [InlineData("""{"enabled":"true"}""", true)]
    [InlineData("""{"enabled":"TRUE"}""", true)]
    [InlineData("""{"enabled":"1"}""", true)]
    [InlineData("""{"enabled":"false"}""", false)]
    [InlineData("""{"enabled":"FALSE"}""", false)]
    [InlineData("""{"enabled":"0"}""", false)]
    public void GetBool_AcceptsBooleanAndStringForms(string json, bool expected)
    {
        var args = TestSupport.Json(json);

        Assert.Equal(expected, args.GetBool("enabled", !expected));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"enabled":"yes"}""")]
    [InlineData("""{"enabled":2}""")]
    public void GetBool_UsesFallbackForMissingOrUnknownForms(string json)
    {
        var args = TestSupport.Json(json);

        Assert.True(args.GetBool("enabled", true));
        Assert.False(args.GetBool("enabled", false));
    }
}
