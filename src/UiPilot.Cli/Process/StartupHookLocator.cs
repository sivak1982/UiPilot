using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UiPilot.Client.Process;

/// <summary>
/// Resolves <c>DOTNET_STARTUP_HOOKS</c> assemblies shipped next to the CLI
/// (<c>hooks/avalonia</c>, <c>hooks/wpf</c>) for zero-edit target-app adoption.
/// </summary>
public static class StartupHookLocator
{
    public const string EnvVarName = "DOTNET_STARTUP_HOOKS";
    public const string DisableEnvVarName = "UIPILOT_STARTUP_HOOK";

    /// <summary>Returns false when env <c>UIPILOT_STARTUP_HOOK=0</c> (or false/no/off).</summary>
    public static bool IsHookEnabledByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(DisableEnvVarName);
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return !(string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Infer <c>avalonia</c> or <c>wpf</c> from assemblies beside the target app.
    /// </summary>
    public static string? DetectUiFramework(string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(appDirectory) || !Directory.Exists(appDirectory))
            return null;

        if (File.Exists(Path.Combine(appDirectory, "Avalonia.dll"))
            || File.Exists(Path.Combine(appDirectory, "Avalonia.Controls.dll"))
            || File.Exists(Path.Combine(appDirectory, "Avalonia.Base.dll")))
        {
            return UiFrameworks.Avalonia;
        }

        if (File.Exists(Path.Combine(appDirectory, "PresentationFramework.dll"))
            || File.Exists(Path.Combine(appDirectory, "PresentationCore.dll")))
        {
            return UiFrameworks.Wpf;
        }

        return null;
    }

    /// <summary>
    /// Full path to the startup-hook DLL for <paramref name="uiFramework"/>, or null if missing.
    /// </summary>
    public static string? ResolveHookAssemblyPath(string uiFramework, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(uiFramework))
            return null;

        var root = baseDirectory ?? AppContext.BaseDirectory;
        var folder = string.Equals(uiFramework, UiFrameworks.Wpf, StringComparison.OrdinalIgnoreCase)
            ? "wpf"
            : string.Equals(uiFramework, UiFrameworks.Avalonia, StringComparison.OrdinalIgnoreCase)
                ? "avalonia"
                : null;
        if (folder is null)
            return null;

        var fileName = folder == "wpf"
            ? "UiPilot.Wpf.StartupHook.dll"
            : "UiPilot.Avalonia.StartupHook.dll";

        var path = Path.GetFullPath(Path.Combine(root, "hooks", folder, fileName));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Sets <c>DOTNET_STARTUP_HOOKS</c> on <paramref name="psi"/> when a hook is available.
    /// Returns the hook path that was set, or null when skipped.
    /// </summary>
    public static string? ApplyTo(
        System.Diagnostics.ProcessStartInfo psi,
        string appDirectory,
        string? uiFramework,
        bool useStartupHook,
        string? baseDirectory = null)
    {
        if (!useStartupHook || !IsHookEnabledByEnvironment())
            return null;

        var framework = string.IsNullOrWhiteSpace(uiFramework)
            ? DetectUiFramework(appDirectory)
            : uiFramework!.Trim();

        if (framework is null)
            return null;

        var hookPath = ResolveHookAssemblyPath(framework, baseDirectory);
        if (hookPath is null)
            return null;

        // Append if the caller already set hooks (rare); otherwise set ours alone.
        var existing = psi.Environment.TryGetValue(EnvVarName, out var prior) ? prior : null;
        if (string.IsNullOrWhiteSpace(existing))
            psi.Environment[EnvVarName] = hookPath;
        else if (existing!.IndexOf(hookPath, StringComparison.OrdinalIgnoreCase) < 0)
            psi.Environment[EnvVarName] = existing + PathListSeparator + hookPath;

        return hookPath;
    }

    private static char PathListSeparator =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
}
