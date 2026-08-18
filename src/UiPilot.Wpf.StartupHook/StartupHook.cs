using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

/// <summary>
/// Loaded via <c>DOTNET_STARTUP_HOOKS</c> before the target app's <c>Main</c>.
/// Uses reflection + a private <see cref="AssemblyLoadContext"/> so UiPilot/MCP/System.Text.Json
/// can load beside the app's shared-framework assemblies, while WPF types stay shared
/// with the target app.
/// Must live in the global namespace with this type and method name (runtime contract).
/// </summary>
internal static class StartupHook
{
    private const int PollMs = 50;
    private const int MaxAttempts = 600; // ~30s
    private const int DispatcherReadMs = 250;

    private static string? _lastPollError;

    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null);

        var thread = new Thread(WaitAndStart)
        {
            IsBackground = true,
            Name = "UiPilot.Wpf.StartupHook",
        };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void WaitAndStart()
    {
        for (var i = 0; i < MaxAttempts; i++)
        {
            try
            {
                if (TryGetWpfMainWindow() != null)
                {
                    StartPilot();
                    return;
                }
            }
            catch (Exception ex)
            {
                // The same failure repeats every poll; log it once so the log stays readable.
                var text = ex.ToString();
                if (!string.Equals(text, _lastPollError, StringComparison.Ordinal))
                {
                    _lastPollError = text;
                    Log("poll: " + text);
                }
            }

            Thread.Sleep(PollMs);
        }

        Log("timed out waiting for MainWindow.");
    }

    private static object? TryGetWpfApplicationCurrent()
    {
        var appType = FindLoadedType("System.Windows.Application");
        if (appType is null)
            return null;
        return appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    /// <summary>
    /// Reads <c>Application.MainWindow</c>. <c>Application</c> is a <c>DispatcherObject</c>, so
    /// reading it from this polling thread throws "The calling thread cannot access this object".
    /// The read is marshalled onto the app's dispatcher and bounded by a timeout: while the
    /// message loop has not started pumping yet the queued read simply does not complete, and
    /// returning null lets the caller retry on the next poll.
    /// </summary>
    private static object? TryGetWpfMainWindow()
    {
        var app = TryGetWpfApplicationCurrent();
        if (app is null)
            return null;

        var mainWindow = app.GetType().GetProperty("MainWindow");
        if (mainWindow is null)
            return null;

        var dispatcher = app.GetType().GetProperty("Dispatcher")?.GetValue(app);
        if (dispatcher is null)
            return null;

        var beginInvoke = dispatcher.GetType().GetMethod(
            "BeginInvoke", new[] { typeof(Delegate), typeof(object[]) });
        if (beginInvoke is null)
            return null;

        object? window = null;
        // Not disposed on purpose: a read that completes after the timeout still signals it.
        var completed = new ManualResetEventSlim(false);
        var read = new Action(() =>
        {
            try { window = mainWindow.GetValue(app); }
            catch { window = null; }
            finally { completed.Set(); }
        });

        beginInvoke.Invoke(dispatcher, new object[] { read, Array.Empty<object>() });

        return completed.Wait(DispatcherReadMs) ? window : null;
    }

    private static void StartPilot()
    {
        try
        {
            var hookDir = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)
                          ?? AppContext.BaseDirectory;
            var pilotPath = Path.Combine(hookDir, "UiPilot.Wpf.dll");
            if (!File.Exists(pilotPath))
            {
                Log("missing " + pilotPath);
                return;
            }

            var alc = new PilotLoadContext(hookDir);
            var asm = alc.LoadFromAssemblyPath(pilotPath);
            var hostType = asm.GetType("UiPilot.Wpf.PilotHost", throwOnError: true)!;
            var start = hostType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null)
                        ?? throw new MissingMethodException(hostType.FullName, "Start(bool)");
            start.Invoke(null, new object[] { true });
            Log("PilotHost.Start(force: true) invoked.");
        }
        catch (Exception ex)
        {
            Log("PilotHost.Start failed: " + ex);
        }
    }

    private static Type? FindLoadedType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName, throwOnError: false))
            .FirstOrDefault(t => t != null);

    private static void Log(string message)
    {
        Debug.WriteLine("[UiPilot.Wpf.StartupHook] " + message);
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "uipilot-startup-hook.log");
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " wpf " + message + Environment.NewLine);
        }
        catch
        {
            // ignore
        }
    }

    private sealed class PilotLoadContext : AssemblyLoadContext
    {
        private readonly string _hookDir;
        private readonly AssemblyDependencyResolver _resolver;

        public PilotLoadContext(string hookDir)
            : base("UiPilot.Wpf", isCollectible: false)
        {
            _hookDir = hookDir;
            _resolver = new AssemblyDependencyResolver(Path.Combine(hookDir, "UiPilot.Wpf.dll"));
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var simple = assemblyName.Name;
            if (string.IsNullOrEmpty(simple))
                return null;

            if (ShouldShareWithApp(simple))
            {
                foreach (var loaded in Default.Assemblies)
                {
                    if (string.Equals(loaded.GetName().Name, simple, StringComparison.OrdinalIgnoreCase))
                        return loaded;
                }

                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName)
                       ?? Path.Combine(_hookDir, simple + ".dll");
            if (path is not null && File.Exists(path))
                return LoadFromAssemblyPath(path);

            return null;
        }

        private static bool ShouldShareWithApp(string simple) =>
            simple is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml"
            || simple.StartsWith("PresentationFramework.", StringComparison.Ordinal)
            || simple.StartsWith("UIAutomation", StringComparison.Ordinal)
            || simple.StartsWith("System.Windows", StringComparison.Ordinal);
    }
}
