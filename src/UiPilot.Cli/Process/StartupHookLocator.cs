using System.Runtime.InteropServices;

namespace UiPilot.Client.Process;

/// <summary>
/// Resolves the generic <c>DOTNET_STARTUP_HOOKS</c> assembly shipped next to the CLI.
/// Framework selection happens inside the target process after a live UI is observed.
/// </summary>
public static class StartupHookLocator
{
    public const string EnvVarName = "DOTNET_STARTUP_HOOKS";
    public const string DisableEnvVarName = "UIPILOT_STARTUP_HOOK";
    public const string FrameworkOverrideEnvVarName = "UIPILOT_UI_FRAMEWORK";

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

    /// <summary>Full path to the generic startup-hook DLL, or null if it is missing.</summary>
    public static string? ResolveHookAssemblyPath(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(root, "hooks", "UiPilot.StartupHook.dll"));
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

        _ = appDirectory; // Kept for source compatibility; runtime probes now select the adapter.
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var hookPath = ResolveHookAssemblyPath(baseDirectory);
        if (hookPath is null)
        {
            throw new FileNotFoundException(
                "UiPilot startup hook DLL was not found next to the CLI (hooks/UiPilot.StartupHook.dll). " +
                "Install UiPilot or pass useStartupHook: false when the app calls PilotHost.Start itself.",
                Path.Combine(root, "hooks", "UiPilot.StartupHook.dll"));
        }

        if (!string.IsNullOrWhiteSpace(uiFramework))
            psi.Environment[FrameworkOverrideEnvVarName] = NormalizeFramework(uiFramework);

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

    private static string NormalizeFramework(string value)
    {
        var framework = value.Trim().ToLowerInvariant();
        if (framework is UiFrameworks.Wpf or UiFrameworks.Avalonia or UiFrameworks.WinForms)
            return framework;
        throw new ArgumentException(
            $"Unknown UI framework '{value}'. Use wpf, avalonia, or winforms.",
            nameof(value));
    }
}
