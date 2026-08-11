using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UiPilot.Cli.Process;

/// <summary>
/// A Windows job object holding a launched process and everything it spawns.
/// <para>
/// <c>Process.Kill(entireProcessTree: true)</c> only walks parent/child links that still exist, so
/// grandchildren survive when the direct child exits first or detaches (e.g. a supervisor console
/// that launches gRPC service hosts). Terminating the job kills every descendant regardless.
/// </para>
/// <para>
/// The job deliberately does not set <c>KILL_ON_JOB_CLOSE</c>: sessions must outlive this CLI
/// process so <c>keepOpen</c> scenarios and separate <c>attach</c> calls keep working.
/// </para>
/// </summary>
public sealed class ProcessJob : IDisposable
{
    private IntPtr _handle;

    private ProcessJob(IntPtr handle) => _handle = handle;

    /// <summary>Creates a job and puts <paramref name="process"/> in it, or null when unsupported.</summary>
    public static ProcessJob? TryCreateFor(System.Diagnostics.Process process, string name)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) return null;

        var job = new ProcessJob(handle);
        try
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return job;
        }
        catch
        {
            // Already in an incompatible job, access denied, or the process exited: fall back to
            // tree-kill semantics rather than failing the launch.
            job.Dispose();
            return null;
        }
    }

    /// <summary>Kills every process still in the job. Safe to call repeatedly.</summary>
    public void Terminate()
    {
        if (_handle == IntPtr.Zero) return;
        try { TerminateJobObject(_handle, 0); }
        catch { /* nothing left to kill */ }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
            CloseHandle(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
