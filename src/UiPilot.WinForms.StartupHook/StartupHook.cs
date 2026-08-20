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
/// can load beside the app's shared-framework assemblies, while WinForms types stay shared
/// with the target app.
/// Must live in the global namespace with this type and method name (runtime contract).
/// </summary>
internal static class StartupHook
{
    private const int PollMs = 50;
    private const int MaxAttempts = 600; // ~30s when no form has been created
    private const int FormMarshalMs = 250;

    private static string? _lastPollError;

    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null);

        var thread = new Thread(WaitAndStart)
        {
            IsBackground = true,
            Name = "UiPilot.WinForms.StartupHook",
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
                if (TryGetReadyForm() != null)
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

        Log("timed out waiting for a handle-created Form.");
    }

    /// <summary>
    /// Finds a form through <c>Application.OpenForms</c> without taking a compile-time dependency
    /// on WinForms. A handle alone does not prove that the UI message loop is pumping, so a small
    /// read is marshalled to the form's owning thread and bounded by a timeout. If it does not run,
    /// the caller retries on the next poll.
    /// </summary>
    private static object? TryGetReadyForm()
    {
        var applicationType = FindLoadedType("System.Windows.Forms.Application");
        if (applicationType is null)
            return null;

        var openForms = applicationType
            .GetProperty("OpenForms", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null);
        if (openForms is null)
            return null;

        var collectionType = openForms.GetType();
        var countProperty = collectionType.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        var itemProperty = collectionType.GetProperty(
            "Item",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            returnType: null,
            types: new[] { typeof(int) },
            modifiers: null);
        if (countProperty is null || itemProperty is null)
            return null;

        var count = (int)(countProperty.GetValue(openForms) ?? 0);
        for (var i = 0; i < count; i++)
        {
            var form = itemProperty.GetValue(openForms, new object[] { i });
            if (form is null || !ReadBooleanProperty(form, "IsHandleCreated"))
                continue;

            var beginInvoke = form.GetType().GetMethod(
                "BeginInvoke",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(Delegate) },
                modifiers: null);
            if (beginInvoke is null)
                continue;

            object? readyForm = null;
            // Not disposed on purpose: an invocation that completes after the timeout still signals it.
            var completed = new ManualResetEventSlim(false);
            var read = new Action(() =>
            {
                try
                {
                    if (!ReadBooleanProperty(form, "IsDisposed")
                        && ReadBooleanProperty(form, "IsHandleCreated"))
                    {
                        readyForm = form;
                    }
                }
                catch
                {
                    readyForm = null;
                }
                finally
                {
                    completed.Set();
                }
            });

            beginInvoke.Invoke(form, new object[] { read });
            if (completed.Wait(FormMarshalMs) && readyForm != null)
                return readyForm;
        }

        return null;
    }

    private static bool ReadBooleanProperty(object instance, string propertyName) =>
        instance.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(instance) is true;

    private static void StartPilot()
    {
        try
        {
            var hookDir = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)
                          ?? AppContext.BaseDirectory;
            var pilotPath = Path.Combine(hookDir, "UiPilot.WinForms.dll");
            if (!File.Exists(pilotPath))
            {
                Log("missing " + pilotPath);
                return;
            }

            var alc = new PilotLoadContext(hookDir);
            var asm = alc.LoadFromAssemblyPath(pilotPath);
            var hostType = asm.GetType("UiPilot.WinForms.PilotHost", throwOnError: true)!;
            var start = hostType.GetMethod(
                            "Start",
                            BindingFlags.Public | BindingFlags.Static,
                            binder: null,
                            types: new[] { typeof(bool) },
                            modifiers: null)
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
        Debug.WriteLine("[UiPilot.WinForms.StartupHook] " + message);
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "uipilot-startup-hook.log");
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " winforms " + message + Environment.NewLine);
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
            : base("UiPilot.WinForms", isCollectible: false)
        {
            _hookDir = hookDir;
            _resolver = new AssemblyDependencyResolver(Path.Combine(hookDir, "UiPilot.WinForms.dll"));
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
            simple.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
            || simple.StartsWith("System.Drawing", StringComparison.OrdinalIgnoreCase)
            || simple.StartsWith("Accessibility", StringComparison.OrdinalIgnoreCase)
            || simple.StartsWith("UIAutomation", StringComparison.OrdinalIgnoreCase);
    }
}
