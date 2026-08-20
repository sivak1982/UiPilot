using System.Net;

namespace UiPilot.Cli.Status;

public sealed class StatusOptions
{
    public const int DefaultPort = 17831;
    public const string BindAddress = "127.0.0.1";

    public StatusOptions(string token, int port = DefaultPort, string bindAddress = BindAddress)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("A nonempty status token is required.", nameof(token));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "The status port must be between 1 and 65535.");
        if (!IPAddress.TryParse(bindAddress, out var address) ||
            !address.Equals(IPAddress.Loopback) ||
            !string.Equals(bindAddress, BindAddress, StringComparison.Ordinal))
        {
            throw new ArgumentException("The status service must bind strictly to 127.0.0.1.", nameof(bindAddress));
        }

        Token = token;
        Port = port;
        Prefix = $"http://{BindAddress}:{port}/";
    }

    public string Token { get; }
    public int Port { get; }
    public string Prefix { get; }

    public static StatusOptions? FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable("UIPILOT_STATUS_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var rawPort = Environment.GetEnvironmentVariable("UIPILOT_STATUS_PORT");
        var port = DefaultPort;
        if (!string.IsNullOrWhiteSpace(rawPort) &&
            (!int.TryParse(rawPort, out port) || port is < 1 or > 65535))
        {
            throw new InvalidOperationException(
                $"UIPILOT_STATUS_PORT must be an integer between 1 and 65535 (default {DefaultPort}).");
        }

        return new StatusOptions(token, port);
    }
}
