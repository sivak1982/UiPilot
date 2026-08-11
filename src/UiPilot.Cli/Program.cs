using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UiPilot.Cli;
using UiPilot.Cli.Scenario;
using UiPilot.Cli.Tools;

// Scenario runner mode: `uipilot run <file-or-folder> [--var name=value ...]`.
// Prints a report to stdout and exits 0 (pass) / 1 (fail) / 2 (usage or parse error).
if (args.Length > 0 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
    return await ScenarioCommand.RunAsync(args.Skip(1).ToArray());

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for the MCP JSON-RPC stream; all logging must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ConnectionManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LifecycleTools>()
    .WithTools<ForwardingTools>()
    .WithTools<ScenarioTools>();

await builder.Build().RunAsync();
return 0;
