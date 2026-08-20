using UiPilot.Cli.Status;
using Xunit;

namespace UiPilot.Tests.Status;

public sealed class StatusOptionsTests
{
    [Fact]
    public void Constructor_UsesStrictIpv4LoopbackPrefix()
    {
        var options = new StatusOptions("secret");

        Assert.Equal(StatusOptions.DefaultPort, options.Port);
        Assert.Equal("http://127.0.0.1:17831/", options.Prefix);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("127.0.0.2")]
    public void Constructor_RejectsAnyOtherBindAddress(string address)
    {
        Assert.Throws<ArgumentException>(() => new StatusOptions("secret", bindAddress: address));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Constructor_RejectsInvalidPort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatusOptions("secret", port));
    }
}
