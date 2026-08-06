using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using UiPilot.Server;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class PipeServerIntegrationTests
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
    public async Task Server_HandlesPingDescribeToolTokenAndUnknown()
    {
        var pipeName = "uipilot-test." + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServer(pipeName, Token, BuildRegistry(), _ => { });
        server.Start();

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);

            var encoding = new UTF8Encoding(false);
            using var reader = new StreamReader(client, encoding, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(client, encoding, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            JsonElement Send(string method, string token, object? p)
            {
                var req = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 1,
                    ["method"] = method,
                    ["token"] = token,
                    ["params"] = p ?? new { },
                });
                writer.WriteLine(req);
                var line = reader.ReadLine();
                Assert.NotNull(line);
                using var doc = JsonDocument.Parse(line!);
                return doc.RootElement.Clone();
            }

            // ping
            var ping = Send("ping", Token, null);
            Assert.True(ping.GetProperty("result").GetProperty("pong").GetBoolean());

            // describe
            var describe = Send("describe", Token, null);
            Assert.Equal(3, describe.GetProperty("result").GetProperty("tools").GetArrayLength());

            // tool call
            var echo = Send("echo", Token, new { value = "hi" });
            Assert.Equal("hi", echo.GetProperty("result").GetProperty("value").GetString());

            // wrong token
            var unauthorized = Send("ping", "wrong", null);
            Assert.Equal(RpcCodes.Unauthorized, unauthorized.GetProperty("error").GetProperty("code").GetInt32());

            // unknown method
            var unknown = Send("does_not_exist", Token, null);
            Assert.Equal(RpcCodes.MethodNotFound, unknown.GetProperty("error").GetProperty("code").GetInt32());

            // tool that throws maps to ToolError
            var boom = Send("boom", Token, null);
            Assert.Equal(RpcCodes.ToolError, boom.GetProperty("error").GetProperty("code").GetInt32());
            Assert.Contains("kaboom", boom.GetProperty("error").GetProperty("message").GetString());

            // pilot tool errors include stable machine-readable data
            var stale = Send("stale", Token, null);
            var staleError = stale.GetProperty("error");
            Assert.Equal(RpcCodes.ToolError, staleError.GetProperty("code").GetInt32());
            Assert.Equal(PilotErrorCodes.StaleElement, staleError.GetProperty("data").GetProperty("code").GetString());
            Assert.Equal("refresh handles", staleError.GetProperty("data").GetProperty("hint").GetString());
        }
        finally
        {
            server.Stop();
        }
    }
}
