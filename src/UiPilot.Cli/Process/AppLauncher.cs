using System.Diagnostics;
using System.Text;

namespace UiPilot.Cli.Process;

/// <summary>
/// Builds and launches a desktop UI app (WPF or Avalonia) for the AI edit loop. All child
/// process output is captured (never inherited) so it can't corrupt this process's stdout MCP stream.
/// </summary>
public static class AppLauncher
{
    /// <summary>Build the project and return its output assembly path (TargetPath).</summary>
    public static async Task<string> BuildAsync(string project, string configuration, string? platform, CancellationToken ct)
    {
        var args = new List<string> { "build", project, "-c", configuration };
        if (!string.IsNullOrWhiteSpace(platform))
            args.Add($"-p:Platform={platform}");
        args.Add("--nologo");
        args.Add("--getProperty:TargetPath");

        var (exit, stdout, stderr) = await RunAsync(
            "dotnet",
            args,
            workingDirectory: null,
            ct).ConfigureAwait(false);

        if (exit != 0)
            throw new InvalidOperationException($"Build failed (exit {exit}).\n{stdout}\n{stderr}");

        var targetPath = stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            throw new InvalidOperationException($"Could not resolve build output (TargetPath='{targetPath}').\n{stdout}");
        return targetPath!;
    }

    /// <summary>
    /// Start a built assembly or prebuilt exe with UiPilot force-enabled via environment variable.
    /// Accepts a <c>.dll</c>/<c>.exe</c> path; working directory defaults to the file's folder.
    /// When <paramref name="useStartupHook"/> is true (default), sets process-scoped
    /// <c>DOTNET_STARTUP_HOOKS</c> so the app need not call <c>PilotHost.Start</c> itself.
    /// </summary>
    /// <param name="startMinimized">
    /// When true (default) the app starts minimized so the agent/IDE stays visible. Pass false to
    /// drive it in the foreground, e.g. when a human is watching the run.
    /// </param>
    public static System.Diagnostics.Process Start(
        string targetAssemblyOrExePath,
        string? workingDirectory = null,
        bool useStartupHook = true,
        string? uiFramework = null,
        bool startMinimized = true) =>
        StartCore(
            targetAssemblyOrExePath, workingDirectory, arguments: null, enablePilot: true,
            useStartupHook, uiFramework, startMinimized, showWindow: false);

    /// <summary>
    /// Start a generic (non-pilot) process — console hosts, helpers, etc. Does not set
    /// <c>UIPILOT_*</c> env vars and does not wait for a discovery file.
    /// </summary>
    /// <param name="showWindow">
    /// When true (default) the process gets its own console window, so it appears in the taskbar
    /// and its output stays out of this process's stdout. Pass false to inherit this console.
    /// </param>
    public static System.Diagnostics.Process StartProcess(
        string exePath,
        string? workingDirectory = null,
        string? arguments = null,
        bool showWindow = true) =>
        StartCore(
            exePath, workingDirectory, arguments, enablePilot: false, useStartupHook: false,
            uiFramework: null, startMinimized: false, showWindow);

    private static System.Diagnostics.Process StartCore(
        string targetAssemblyOrExePath,
        string? workingDirectory,
        string? arguments,
        bool enablePilot,
        bool useStartupHook,
        string? uiFramework,
        bool startMinimized,
        bool showWindow)
    {
        if (string.IsNullOrWhiteSpace(targetAssemblyOrExePath))
            throw new ArgumentException("Target path is required.", nameof(targetAssemblyOrExePath));

        var fullPath = Path.GetFullPath(targetAssemblyOrExePath);
        var exePath = string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.ChangeExtension(fullPath, ".exe");

        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetDirectoryName(fullPath)
            : Path.GetFullPath(workingDirectory!);

        var psi = new ProcessStartInfo
        {
            // ShellExecute gives a console host its own window (and taskbar button) instead of
            // sharing ours, which also keeps its output out of this process's stdout MCP stream.
            // It rules out psi.Environment, so it is only used for non-pilot processes.
            UseShellExecute = showWindow && !enablePilot,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = workDir,
        };

        if (enablePilot)
        {
            psi.Environment["UIPILOT_ENABLE"] = "1";
            // Keep the driven app out of the way so the agent/IDE stays visible. Offscreen screenshots
            // still work while minimized; use the bring_to_front tool to show it on demand.
            if (startMinimized)
                psi.Environment["UIPILOT_START_MINIMIZED"] = "1";

            var appDir = workDir ?? Path.GetDirectoryName(fullPath) ?? ".";
            var hookPath = StartupHookLocator.ApplyTo(psi, appDir, uiFramework, useStartupHook);
            if (hookPath != null)
                Debug.WriteLine("[UiPilot.Cli] DOTNET_STARTUP_HOOKS=" + hookPath);
        }

        if (File.Exists(exePath))
        {
            psi.FileName = exePath;
            if (!string.IsNullOrWhiteSpace(arguments))
                psi.Arguments = arguments;
        }
        else if (File.Exists(fullPath))
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(fullPath);
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                foreach (var part in SplitArguments(arguments))
                    psi.ArgumentList.Add(part);
            }
        }
        else
        {
            throw new FileNotFoundException($"App path not found: {targetAssemblyOrExePath}", targetAssemblyOrExePath);
        }

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the app process.");

        // Track descendants from the moment the process starts, so stopping the session also stops
        // whatever it spawns (service hosts, helper processes) even after it has exited itself.
        var job = ProcessJob.TryCreateFor(process, $"uipilot-{process.Id}");
        if (job != null)
            Jobs[process.Id] = job;

        return process;
    }

    /// <summary>Minimal argument splitter for optional <c>dotnet dll …</c> extras; keeps quoted spans intact.</summary>
    private static IEnumerable<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }

    private static async Task<(int exit, string stdout, string stderr)> RunAsync(
        string fileName, IEnumerable<string> args, string? workingDirectory, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory!;

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public static void KillTree(System.Diagnostics.Process? process)
    {
        if (process == null) return;
        try
        {
            TerminateJob(process.Id);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Kill a process (and its tree) by pid. Used to stop apps that were attached to rather than
    /// launched by this CLI. Killing an elevated target requires this CLI to also be elevated.
    /// </summary>
    public static void KillByPid(int pid)
    {
        TerminateJob(pid);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore: already exited, or access denied (CLI not elevated for an elevated target)
        }
    }

    /// <summary>Jobs for CLI-launched processes, keyed by pid. See <see cref="ProcessJob"/>.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ProcessJob> Jobs = new();

    private static void TerminateJob(int pid)
    {
        if (!Jobs.TryRemove(pid, out var job)) return;
        job.Terminate();
        job.Dispose();
    }
}
