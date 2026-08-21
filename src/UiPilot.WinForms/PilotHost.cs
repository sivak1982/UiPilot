using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using UiPilot.Hosting;
using UiPilot.Tools;

namespace UiPilot.WinForms;

/// <summary>Single public entry point for Windows Forms applications.</summary>
public static class PilotHost
{
    public const string ProtocolVersion = PilotRuntime.ProtocolVersion;

    private static readonly object Gate = new object();
    private static readonly PilotRuntime Runtime = new PilotRuntime();
    private static bool _hooksAttached;
    private static bool _startPending;
    private static bool _starting;

    public static bool IsRunning => Runtime.IsRunning;
    public static ToolRegistry? Tools => Runtime.Tools;

    public static void Start() => Start((WinFormsOptions?)null);
    public static void Start(bool force) => Start(new WinFormsOptions { Force = force });

    /// <summary>
    /// Starts the pilot. Call from the UI thread after the first form has been created.
    /// The method is idempotent.
    /// </summary>
    public static void Start(WinFormsOptions? options)
    {
        options ??= new WinFormsOptions();
        Form? marshalControl;
        lock (Gate)
        {
            if (Runtime.IsRunning || _starting)
                return;

            marshalControl = FirstForm();
            if (marshalControl == null)
            {
                if (!_startPending)
                {
                    _startPending = true;
                    EventHandler? retry = null;
                    retry = (_, _) =>
                    {
                        Application.Idle -= retry;
                        lock (Gate) _startPending = false;
                        Start(options);
                    };
                    Application.Idle += retry;
                    Log("UiPilot.WinForms will start when the application message loop becomes idle.");
                }
                return;
            }
            _starting = true;
        }

        var context = SynchronizationContext.Current;
        var backend = new WinFormsUiBackend();
        try
        {
            object? Invoke(Func<object?> func)
            {
                var currentForm = FirstForm();
                if (currentForm != null && !currentForm.IsDisposed)
                {
                    if (!currentForm.InvokeRequired)
                        return func();
                    return currentForm.Invoke(func);
                }

                if (context == null)
                    throw new InvalidOperationException(
                        "No live WinForms form or UI synchronization context is available.");

                object? result = null;
                Exception? error = null;
                context.Send(_ =>
                {
                    try { result = func(); }
                    catch (Exception ex) { error = ex; }
                }, null);
                if (error != null) throw error;
                return result;
            }

            var pilotOptions = options.ToPilotOptions();
            var started = Runtime.Start(
                pilotOptions,
                backend,
                Invoke,
                () => FirstForm()?.Text,
                Log);

            if (!started)
                return;

            lock (Gate)
            {
                if (!_hooksAttached)
                {
                    _hooksAttached = true;
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
                    Application.ApplicationExit += (_, _) => Stop();
                }
            }

            if (PilotRuntime.ResolveStartMinimized(pilotOptions))
                marshalControl.BeginInvoke(new Action(() =>
                {
                    var form = FirstForm();
                    if (form != null) form.WindowState = FormWindowState.Minimized;
                }));
        }
        catch
        {
            if (!Runtime.IsRunning)
                try { backend.Shutdown(); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            lock (Gate) _starting = false;
        }
    }

    public static void Stop() => Runtime.Stop(Log);

    internal static Form? FirstForm()
    {
        foreach (Form form in Application.OpenForms)
            return form;
        return null;
    }

    private static void Log(string message) => Debug.WriteLine("[UiPilot.WinForms] " + message);
}
