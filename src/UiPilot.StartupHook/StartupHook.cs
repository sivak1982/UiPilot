using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

/// <summary>
/// Loaded through DOTNET_STARTUP_HOOKS before the target application's Main method.
/// This type must remain in the global namespace with this exact method contract.
/// </summary>
internal static class StartupHook
{
    private const int PollMilliseconds = 50;
    private const int MarshalTimeoutMilliseconds = 250;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly object LogLock = new();
    private static readonly HashSet<string> LoggedMessages = new(StringComparer.Ordinal);

    public static void Initialize()
    {
        // Do not let child processes inherit this hook.
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null);

        var thread = new Thread(WaitAndStart)
        {
            IsBackground = true,
            Name = "UiPilot.StartupHook",
        };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void WaitAndStart()
    {
        var requestedFramework = NormalizeFramework(
            Environment.GetEnvironmentVariable("UIPILOT_UI_FRAMEWORK"));
        if (requestedFramework is null
            && !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("UIPILOT_UI_FRAMEWORK")))
        {
            Log("invalid UIPILOT_UI_FRAMEWORK; expected wpf, avalonia, or winforms.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < StartupTimeout)
        {
            var framework = requestedFramework is null
                ? ProbeFirstReadyFramework()
                : ProbeFramework(requestedFramework) ? requestedFramework : null;

            if (framework is not null)
            {
                StartPilot(framework);
                return;
            }

            Thread.Sleep(PollMilliseconds);
        }

        Log(requestedFramework is null
            ? "timed out waiting for a supported UI."
            : "timed out waiting for " + requestedFramework + " UI.");
    }

    private static string? ProbeFirstReadyFramework()
    {
        // Stable tie order for processes that have more than one live UI framework.
        if (ProbeFramework("avalonia"))
            return "avalonia";
        if (ProbeFramework("wpf"))
            return "wpf";
        if (ProbeFramework("winforms"))
            return "winforms";
        return null;
    }

    private static bool ProbeFramework(string framework)
    {
        try
        {
            return framework switch
            {
                "avalonia" => TryGetAvaloniaMainWindow() is not null,
                "wpf" => TryGetWpfMainWindow() is not null,
                "winforms" => TryGetReadyWinFormsForm() is not null,
                _ => false,
            };
        }
        catch (Exception ex)
        {
            Log(framework + " poll: " + ex);
            return false;
        }
    }

    private static object? TryGetAvaloniaMainWindow()
    {
        var applicationType = FindLoadedType("Avalonia.Application");
        var application = applicationType?
            .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null);
        if (application is null)
            return null;

        var lifetime = application.GetType()
            .GetProperty("ApplicationLifetime", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(application);
        return lifetime?.GetType()
            .GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(lifetime);
    }

    private static object? TryGetWpfMainWindow()
    {
        var applicationType = FindLoadedType("System.Windows.Application");
        var application = applicationType?
            .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null);
        if (application is null)
            return null;

        var mainWindow = application.GetType()
            .GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Instance);
        var dispatcher = application.GetType()
            .GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(application);
        if (mainWindow is null || dispatcher is null)
            return null;

        var beginInvoke = dispatcher.GetType().GetMethod(
            "BeginInvoke",
            new[] { typeof(Delegate), typeof(object[]) });
        if (beginInvoke is null)
            return null;

        object? window = null;
        // A queued callback can complete after the bounded wait, so this event is not disposed.
        var completed = new ManualResetEventSlim(false);
        var read = new Action(() =>
        {
            try
            {
                window = mainWindow.GetValue(application);
            }
            catch
            {
                window = null;
            }
            finally
            {
                completed.Set();
            }
        });

        beginInvoke.Invoke(dispatcher, new object[] { read, Array.Empty<object>() });
        return completed.Wait(MarshalTimeoutMilliseconds) ? window : null;
    }

    private static object? TryGetReadyWinFormsForm()
    {
        var applicationType = FindLoadedType("System.Windows.Forms.Application");
        var openForms = applicationType?
            .GetProperty("OpenForms", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null);
        if (openForms is null)
            return null;

        var collectionType = openForms.GetType();
        var countProperty = collectionType.GetProperty(
            "Count",
            BindingFlags.Public | BindingFlags.Instance);
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
        for (var index = 0; index < count; index++)
        {
            var form = itemProperty.GetValue(openForms, new object[] { index });
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
            // A queued callback can complete after the bounded wait, so this event is not disposed.
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
            if (completed.Wait(MarshalTimeoutMilliseconds) && readyForm is not null)
                return readyForm;
        }

        return null;
    }

    private static bool ReadBooleanProperty(object instance, string propertyName) =>
        instance.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(instance) is true;

    private static Type? FindLoadedType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(type => type is not null);

    private static string? NormalizeFramework(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "avalonia" or "wpf" or "winforms" ? normalized : null;
    }

    private static void StartPilot(string framework)
    {
        try
        {
            var hookDirectory = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)
                                ?? AppContext.BaseDirectory;
            var frameworkName = framework switch
            {
                "avalonia" => "Avalonia",
                "wpf" => "Wpf",
                "winforms" => "WinForms",
                _ => throw new ArgumentOutOfRangeException(nameof(framework)),
            };
            var payloadDirectory = Path.Combine(hookDirectory, framework);
            var payloadPath = Path.Combine(
                payloadDirectory,
                "UiPilot." + frameworkName + ".dll");
            if (!File.Exists(payloadPath))
            {
                Log("missing " + payloadPath);
                return;
            }

            var loadContext = new PilotLoadContext(
                framework,
                payloadDirectory,
                payloadPath);
            var assembly = loadContext.LoadFromAssemblyPath(payloadPath);
            var hostType = assembly.GetType(
                "UiPilot." + frameworkName + ".PilotHost",
                throwOnError: true)!;
            var start = hostType.GetMethod(
                            "Start",
                            BindingFlags.Public | BindingFlags.Static,
                            binder: null,
                            types: new[] { typeof(bool) },
                            modifiers: null)
                        ?? throw new MissingMethodException(hostType.FullName, "Start(bool)");

            start.Invoke(null, new object[] { true });
            Log(framework + " PilotHost.Start(force: true) invoked.");
        }
        catch (Exception ex)
        {
            Log(framework + " PilotHost.Start failed: " + ex);
        }
    }

    private static void Log(string message)
    {
        lock (LogLock)
        {
            if (!LoggedMessages.Add(message))
                return;

            Debug.WriteLine("[UiPilot.StartupHook] " + message);
            try
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    "uipilot-startup-hook.log");
                File.AppendAllText(
                    path,
                    DateTime.UtcNow.ToString("o") + " generic " + message
                    + Environment.NewLine);
            }
            catch
            {
                // Startup-hook diagnostics must never prevent the target app from starting.
            }
        }
    }

    private sealed class PilotLoadContext : AssemblyLoadContext
    {
        private readonly string _framework;
        private readonly string _payloadDirectory;
        private readonly AssemblyDependencyResolver _resolver;

        public PilotLoadContext(
            string framework,
            string payloadDirectory,
            string payloadPath)
            : base("UiPilot." + framework, isCollectible: false)
        {
            _framework = framework;
            _payloadDirectory = payloadDirectory;
            _resolver = new AssemblyDependencyResolver(payloadPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var simpleName = assemblyName.Name;
            if (string.IsNullOrEmpty(simpleName))
                return null;

            if (ShouldShareWithApplication(simpleName))
            {
                foreach (var loaded in Default.Assemblies)
                {
                    if (string.Equals(
                            loaded.GetName().Name,
                            simpleName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return loaded;
                    }
                }

                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName)
                       ?? Path.Combine(_payloadDirectory, simpleName + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        private bool ShouldShareWithApplication(string simpleName) =>
            _framework switch
            {
                "avalonia" =>
                    simpleName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                    || simpleName.StartsWith("MicroCom", StringComparison.OrdinalIgnoreCase)
                    || simpleName.StartsWith("HarfBuzz", StringComparison.OrdinalIgnoreCase)
                    || simpleName.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase)
                    || simpleName.StartsWith("Tmds.", StringComparison.OrdinalIgnoreCase),
                "wpf" =>
                    simpleName is "PresentationFramework"
                        or "PresentationCore"
                        or "WindowsBase"
                        or "System.Xaml"
                    || simpleName.StartsWith(
                        "PresentationFramework.",
                        StringComparison.Ordinal)
                    || simpleName.StartsWith("UIAutomation", StringComparison.Ordinal)
                    || simpleName.StartsWith("System.Windows", StringComparison.Ordinal),
                "winforms" =>
                    simpleName.StartsWith(
                        "System.Windows.Forms",
                        StringComparison.OrdinalIgnoreCase)
                    || simpleName.StartsWith(
                        "System.Drawing",
                        StringComparison.OrdinalIgnoreCase)
                    || simpleName.StartsWith(
                        "Accessibility",
                        StringComparison.OrdinalIgnoreCase)
                    || simpleName.StartsWith(
                        "UIAutomation",
                        StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
    }
}
