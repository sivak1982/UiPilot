using System.IO.Pipes;
using UiPilot.Server;
using Xunit;

namespace UiPilot.Tests;

public class PipeSessionAuthTests
{
    [Fact]
    public async Task Auth_RoundTripsWithMatchingToken()
    {
        var pipeName = "uipilot-auth." + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            Assert.True(await PipeSessionAuth.TryAuthenticateServerAsync(server, "secret"));
        });

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        await PipeSessionAuth.WriteClientAsync(client, "secret", CancellationToken.None);
        await serverTask;
    }

    [Fact]
    public async Task Auth_RejectsWrongToken()
    {
        var pipeName = "uipilot-auth-bad." + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            Assert.False(await PipeSessionAuth.TryAuthenticateServerAsync(server, "secret"));
        });

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PipeSessionAuth.WriteClientAsync(client, "wrong", CancellationToken.None));
        await serverTask;
    }
}
