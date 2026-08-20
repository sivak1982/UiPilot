using System.Text.Json;
using ModelContextProtocol.Protocol;
using UiPilot.Client;

namespace UiPilot.Cli.Status;

public sealed class OperationTelemetry
{
    private readonly OperationHub _hub;

    public OperationTelemetry(OperationHub hub) => _hub = hub;

    public CallToolResult Run(
        string name,
        string category,
        string? session,
        Func<CallToolResult> operation)
    {
        var scope = _hub.Start(name, category, session);
        try
        {
            var result = operation();
            Complete(scope, result);
            return result;
        }
        catch (Exception ex)
        {
            scope.Fail(ErrorCode(ex), "Operation failed.");
            throw;
        }
    }

    public async Task<CallToolResult> RunAsync(
        string name,
        string category,
        string? session,
        Func<Task<CallToolResult>> operation)
    {
        var scope = _hub.Start(name, category, session);
        try
        {
            var result = await operation().ConfigureAwait(false);
            Complete(scope, result);
            return result;
        }
        catch (Exception ex)
        {
            scope.Fail(ErrorCode(ex), "Operation failed.");
            throw;
        }
    }

    public void RecordFailure(string name, string category, string? session, string errorCode)
    {
        var scope = _hub.Start(name, category, session);
        scope.Fail(errorCode, "Operation was rejected.");
    }

    private static void Complete(OperationHub.OperationScope scope, CallToolResult result)
    {
        if (result.IsError == true)
            scope.Fail(ReadResultErrorCode(result), "Operation returned an error.");
        else
            scope.Succeed();
    }

    private static string ReadResultErrorCode(CallToolResult result)
    {
        try
        {
            var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return "tool_error";
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("code", out var code)
                ? code.GetString() ?? "tool_error"
                : "tool_error";
        }
        catch
        {
            return "tool_error";
        }
    }

    private static string ErrorCode(Exception exception) => exception switch
    {
        PilotCliException cli => cli.Code,
        OperationCanceledException => "cancelled",
        TimeoutException => "timeout",
        _ => "unhandled_error",
    };
}
