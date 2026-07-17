using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfPilot.Cli;
using WpfPilot.Cli.Tools;

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for the MCP JSON-RPC stream; all logging must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ConnectionManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LifecycleTools>()
    .WithTools<ForwardingTools>();

await builder.Build().RunAsync();
