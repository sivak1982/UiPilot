using System.Diagnostics;
using System.Text;

namespace WpfPilot.Cli.Process;

/// <summary>
/// Builds and launches a WPF app for the AI edit loop. All child process output is captured
/// (never inherited) so it can't corrupt this process's stdout MCP stream.
/// </summary>
public static class AppLauncher
{
    /// <summary>Build the project and return its output assembly path (TargetPath).</summary>
    public static async Task<string> BuildAsync(string project, string configuration, CancellationToken ct)
    {
        var (exit, stdout, stderr) = await RunAsync(
            "dotnet",
            new[] { "build", project, "-c", configuration, "--nologo", "--getProperty:TargetPath" },
            workingDirectory: null,
            ct).ConfigureAwait(false);

        if (exit != 0)
            throw new InvalidOperationException($"Build failed (exit {exit}).\n{stdout}\n{stderr}");

        var targetPath = stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            throw new InvalidOperationException($"Could not resolve build output (TargetPath='{targetPath}').\n{stdout}");
        return targetPath!;
    }

    /// <summary>Start the built app with WpfPilot force-enabled via environment variable.</summary>
    public static System.Diagnostics.Process Start(string targetAssemblyPath)
    {
        var exePath = Path.ChangeExtension(targetAssemblyPath, ".exe");
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = Path.GetDirectoryName(targetAssemblyPath),
        };
        psi.Environment["WPFPILOT_ENABLE"] = "1";

        if (File.Exists(exePath))
        {
            psi.FileName = exePath;
        }
        else
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(targetAssemblyPath);
        }

        var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the app process.");
        return process;
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
}
