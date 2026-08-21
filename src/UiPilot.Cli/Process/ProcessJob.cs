using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UiPilot.Client.Process;

/// <summary>
/// A Windows job object holding a launched process and everything it spawns.
/// <para>
/// <c>Process.Kill(entireProcessTree: true)</c> only walks parent/child links that still exist, so
/// grandchildren survive when the direct child exits first or detaches (e.g. a supervisor console
/// that launches gRPC service hosts). Terminating the job kills every descendant regardless.
/// </para>
/// <para>
/// The job deliberately does not set <c>KILL_ON_JOB_CLOSE</c>: sessions may intentionally outlive
/// this CLI process and be picked up later by a separate <c>attach</c> call.
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

    /// <summary>
    /// Returns whether the job still owns any live process. Query failures are treated as active
    /// so a transient Win32 error never makes the CLI drop its only descendant-tracking handle.
    /// </summary>
    public bool HasActiveProcesses()
    {
        var handle = _handle;
        if (handle == IntPtr.Zero) return false;
        if (!QueryInformationJobObject(
                handle,
                JOBOBJECTINFOCLASS.JobObjectBasicAccountingInformation,
                out var accounting,
                Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(),
                IntPtr.Zero))
        {
            return true;
        }
        return accounting.ActiveProcesses != 0;
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
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        JOBOBJECTINFOCLASS informationClass,
        out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information,
        int informationLength,
        IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private enum JOBOBJECTINFOCLASS
    {
        JobObjectBasicAccountingInformation = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }
}
