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
    public static System.Diagnostics.Process Start(
        string targetAssemblyOrExePath,
        string? workingDirectory = null,
        bool useStartupHook = true,
        string? uiFramework = null) =>
        StartCore(targetAssemblyOrExePath, workingDirectory, arguments: null, enablePilot: true, useStartupHook, uiFramework);

    /// <summary>
    /// Start a generic (non-pilot) process — console hosts, helpers, etc. Does not set
    /// <c>UIPILOT_*</c> env vars and does not wait for a discovery file.
    /// </summary>
    public static System.Diagnostics.Process StartProcess(
        string exePath,
        string? workingDirectory = null,
        string? arguments = null) =>
        StartCore(exePath, workingDirectory, arguments, enablePilot: false, useStartupHook: false, uiFramework: null);

    private static System.Diagnostics.Process StartCore(
        string targetAssemblyOrExePath,
        string? workingDirectory,
        string? arguments,
        bool enablePilot,
        bool useStartupHook,
        string? uiFramework)
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
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = workDir,
        };

        if (enablePilot)
        {
            psi.Environment["UIPILOT_ENABLE"] = "1";
            // Keep the driven app out of the way so the agent/IDE stays visible. Offscreen screenshots
            // still work while minimized; use the bring_to_front tool to show it on demand.
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
}
