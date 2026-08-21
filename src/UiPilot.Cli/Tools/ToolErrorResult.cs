using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using UiPilot.Client;
using UiPilot.Client.Pipe;
using UiPilot.Tools;

namespace UiPilot.Cli.Tools;

/// <summary>Single exception-to-MCP error envelope mapping for every CLI tool surface.</summary>
internal static class ToolErrorResult
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool TryCreate(Exception exception, out CallToolResult result)
    {
        var error = exception switch
        {
            PilotCliException cli => (cli.Code, cli.Message, cli.Hint),
            PipeRpcException pipe => (
                pipe.Code ?? $"rpc_{pipe.RpcCode}", pipe.Message, pipe.Hint),
            TimeoutException timeout => (
                PilotErrorCodes.Timeout, timeout.Message, (string?)null),
            McpException mcp => ("mcp_error", mcp.Message, (string?)null),
            _ => default,
        };

        if (error == default)
        {
            result = new CallToolResult();
            return false;
        }

        result = new CallToolResult
        {
            IsError = true,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(new
                    {
                        error = true,
                        code = error.Item1,
                        message = error.Item2,
                        hint = error.Item3,
                    }, Json),
                },
            ],
        };
        return true;
    }
}
