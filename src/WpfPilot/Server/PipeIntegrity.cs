using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WpfPilot.Server;

/// <summary>
/// Creates the named pipe with a security descriptor applied at creation time: a permissive DACL
/// plus a Low mandatory integrity label. This lets a lower-integrity agent (e.g. a Medium-IL MCP
/// CLI) connect to a pipe owned by an elevated (High-IL) app - Windows' default "no-write-up"
/// policy otherwise blocks the connect. Requests remain gated by the per-run auth token.
///
/// Setting the label at creation (via SECURITY_ATTRIBUTES) avoids needing WRITE_OWNER on the
/// handle and does not require any special privilege when the label is at/below our own token.
/// </summary>
internal static class PipeIntegrity
{
    // Authenticated Users + Administrators get full access (DACL); Low integrity label (SACL).
    private const string Sddl = "D:(A;;FA;;;AU)(A;;FA;;;BA)S:(ML;;NW;;;LW)";

    private const uint PIPE_ACCESS_DUPLEX = 0x00000003;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint PIPE_TYPE_BYTE = 0x00000000;
    private const uint PIPE_WAIT = 0x00000000;
    private const uint SDDL_REVISION_1 = 1;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    /// <summary>
    /// Create a single-instance async server pipe with the low-integrity descriptor. Falls back to
    /// a plain managed pipe (same-integrity clients only) if the native path fails for any reason.
    /// </summary>
    public static NamedPipeServerStream CreateServer(string pipeName, Action<string> log)
    {
        try
        {
            var stream = TryCreate(pipeName);
            if (stream != null) return stream;
        }
        catch (Exception ex)
        {
            log("WpfPilot: low-integrity pipe unavailable, using default (same-integrity clients only): " + ex.Message);
        }

        return new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    private static NamedPipeServerStream? TryCreate(string pipeName)
    {
        var psd = IntPtr.Zero;
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(Sddl, SDDL_REVISION_1, out psd, out _))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSecurityDescriptor failed");

            var sa = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                lpSecurityDescriptor = psd,
                bInheritHandle = false,
            };

            var handle = CreateNamedPipeW(
                @"\\.\pipe\" + pipeName,
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                PIPE_TYPE_BYTE | PIPE_WAIT,
                nMaxInstances: 1,
                nOutBufferSize: 0,
                nInBufferSize: 0,
                nDefaultTimeOut: 0,
                ref sa);

            if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateNamedPipe failed");

            var safeHandle = new SafePipeHandle(handle, ownsHandle: true);
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, safeHandle);
        }
        finally
        {
            if (psd != IntPtr.Zero) LocalFree(psd);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl, uint revision, out IntPtr securityDescriptor, out uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateNamedPipeW(
        string name, uint openMode, uint pipeMode, uint nMaxInstances,
        uint nOutBufferSize, uint nInBufferSize, uint nDefaultTimeOut, ref SECURITY_ATTRIBUTES sa);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);
}
