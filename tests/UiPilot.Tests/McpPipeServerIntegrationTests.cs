using System.Text.Json;
using UiPilot.Client.Pipe;
using UiPilot.Server;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class McpPipeServerIntegrationTests
{
    private const string Token = "test-token";

    private static ToolRegistry BuildRegistry()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        registry.Register("echo", "echoes value", (_, args) =>
            new { value = args.GetProperty("value").GetString() });
        registry.Register("boom", "always throws", (_, _) =>
            throw new InvalidOperationException("kaboom"));
        registry.Register("stale", "throws pilot error", (_, _) =>
            throw new PilotToolException(PilotErrorCodes.StaleElement, "stale handle", "refresh handles"));
        return registry;
    }

    [Fact]
    public async Task Server_HandlesListToolsCallToolAuthAndErrors()
    {
        var pipeName = "uipilot-mcp-test." + Guid.NewGuid().ToString("N");
        var server = new McpPipeServer(pipeName, Token, BuildRegistry(), _ => { });
        server.Start();

        try
        {
            using var client = await McpPipeClient.ConnectAsync(pipeName, Token);

            await client.PingAsync();

            var describe = await client.ListToolsAsync();
            Assert.Equal(3, describe.GetProperty("tools").GetArrayLength());

            var echo = await client.CallToolAsync("echo", new { value = "hi" });
            Assert.Equal("hi", echo.GetProperty("value").GetString());

            var boom = await Assert.ThrowsAsync<PipeRpcException>(() =>
                client.CallToolAsync("boom", new { }));
            Assert.Equal("tool_error", boom.Code);
            Assert.Contains("kaboom", boom.Message);

            var stale = await Assert.ThrowsAsync<PipeRpcException>(() =>
                client.CallToolAsync("stale", new { }));
            Assert.Equal(PilotErrorCodes.StaleElement, stale.Code);
            Assert.Equal("refresh handles", stale.Hint);

            var unknown = await Assert.ThrowsAsync<PipeRpcException>(() =>
                client.CallToolAsync("does_not_exist", new { }));
            Assert.Equal(PilotErrorCodes.NotFound, unknown.Code);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Connect_RejectsWrongToken()
    {
        var pipeName = "uipilot-mcp-auth." + Guid.NewGuid().ToString("N");
        var server = new McpPipeServer(pipeName, Token, BuildRegistry(), _ => { });
        server.Start();

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                McpPipeClient.ConnectAsync(pipeName, "wrong-token"));
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Server_IsolatesMultipleClients_AndStopDisconnectsThem()
    {
        var pipeName = "uipilot-mcp-multi." + Guid.NewGuid().ToString("N");
        var server = new McpPipeServer(pipeName, Token, BuildRegistry(), _ => { });
        server.Start();
        using var first = await McpPipeClient.ConnectAsync(pipeName, Token);
        using var second = await McpPipeClient.ConnectAsync(pipeName, Token);

        var calls = await Task.WhenAll(
            first.CallToolAsync("echo", new { value = "one" }),
            second.CallToolAsync("echo", new { value = "two" }));
        Assert.Equal("one", calls[0].GetProperty("value").GetString());
        Assert.Equal("two", calls[1].GetProperty("value").GetString());

        first.Dispose();
        var stillConnected = await second.CallToolAsync("echo", new { value = "alive" });
        Assert.Equal("alive", stillConnected.GetProperty("value").GetString());

        server.Stop();
        await Assert.ThrowsAnyAsync<Exception>(
            () => second.CallToolAsync("echo", new { value = "stopped" })
                .WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
