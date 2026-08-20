using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UiPilot.Client;
using UiPilot.Cli.Status;
using UiPilot.Cli.Tools;

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for the MCP JSON-RPC stream; all logging must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<OperationHub>();
builder.Services.AddSingleton<OperationTelemetry>();

var statusOptions = StatusOptions.FromEnvironment();
if (statusOptions is not null)
{
    builder.Services.AddSingleton(statusOptions);
    builder.Services.AddSingleton<IStatusSnapshotSource, ConnectionManagerSnapshotSource>();
    builder.Services.AddHostedService<StatusService>();
}

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LifecycleTools>()
    .WithTools<ForwardingTools>();

await builder.Build().RunAsync();
return 0;
