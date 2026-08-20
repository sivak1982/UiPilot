using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace UiPilot.Client.Process;

/// <summary>
/// Resolves <c>DOTNET_STARTUP_HOOKS</c> assemblies shipped next to the CLI
/// (<c>hooks/avalonia</c>, <c>hooks/wpf</c>, <c>hooks/winforms</c>) for zero-edit target-app adoption.
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
    /// Infer a supported UI framework from assemblies beside the target app.
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

        if (File.Exists(Path.Combine(appDirectory, "System.Windows.Forms.dll")))
            return UiFrameworks.WinForms;

        // Framework-dependent desktop apps do not copy WindowsDesktop assemblies beside the exe.
        // Inspect managed assembly references so a plain WinForms output directory is still
        // auto-detected without requiring callers to pass uiFramework explicitly.
        foreach (var assemblyPath in Directory.EnumerateFiles(appDirectory, "*.dll")
                     .Concat(Directory.EnumerateFiles(appDirectory, "*.exe")))
        {
            if (ReferencesAssembly(assemblyPath, "Avalonia")
                || ReferencesAssembly(assemblyPath, "Avalonia.Controls"))
            {
                return UiFrameworks.Avalonia;
            }
            if (ReferencesAssembly(assemblyPath, "PresentationFramework")
                || ReferencesAssembly(assemblyPath, "PresentationCore"))
            {
                return UiFrameworks.Wpf;
            }
            if (ReferencesAssembly(assemblyPath, "System.Windows.Forms"))
                return UiFrameworks.WinForms;
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
                : string.Equals(uiFramework, UiFrameworks.WinForms, StringComparison.OrdinalIgnoreCase)
                    ? "winforms"
                    : null;
        if (folder is null)
            return null;

        var fileName = folder switch
        {
            "wpf" => "UiPilot.Wpf.StartupHook.dll",
            "avalonia" => "UiPilot.Avalonia.StartupHook.dll",
            _ => "UiPilot.WinForms.StartupHook.dll",
        };

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

    private static bool ReferencesAssembly(string path, string assemblyName)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return false;
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                if (string.Equals(
                        metadata.GetString(reference.Name),
                        assemblyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Native executable or malformed file; it cannot carry managed assembly references.
        }
        catch (IOException)
        {
            // Best-effort detection. The explicit uiFramework parameter remains available.
        }

        return false;
    }
}
