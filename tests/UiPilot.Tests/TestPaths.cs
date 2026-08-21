using System.Runtime.CompilerServices;

namespace UiPilot.Tests;

/// <summary>Shared helpers for locating built sample apps under Debug or Release.</summary>
internal static class TestPaths
{
    public static string Configuration =>
        Environment.GetEnvironmentVariable("UIPILOT_TEST_CONFIGURATION")
        ?? (string.IsNullOrWhiteSpace(BuildConfiguration) ? "Debug" : BuildConfiguration);

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    public static string FindRepoRoot([CallerFilePath] string? callerFile = null)
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory, Path.GetDirectoryName(callerFile) })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "UiPilot.sln")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the UiPilot repository root.");
    }

    public static string SampleApp(
        string sampleFolder,
        string tfm,
        string exeName,
        [CallerFilePath] string? callerFile = null)
    {
        var root = FindRepoRoot(callerFile);
        var preferred = Path.Combine(root, "samples", sampleFolder, "bin", Configuration, tfm, exeName);
        if (File.Exists(preferred))
            return preferred;

        var other = string.Equals(Configuration, "Release", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";
        var fallback = Path.Combine(root, "samples", sampleFolder, "bin", other, tfm, exeName);
        if (File.Exists(fallback))
            return fallback;

        return preferred;
    }
}
