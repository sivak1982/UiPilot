using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace WpfPilot.Interaction;

/// <summary>
/// Real OS-level mouse input via SendInput. Unlike <see cref="SyntheticInput"/>, this goes through
/// hit-testing, mouse capture and the Preview* tunnel, which is the only way to exercise drag
/// interactions (Thumb, DragDrop, manual capture-based drags).
/// <para>
/// Must be called from a background thread: the target app's UI thread has to keep pumping messages
/// while the input is injected, so blocking it would deadlock the drag.
/// </para>
/// </summary>
public static class RealInput
{
    /// <summary>Presses at <paramref name="from"/>, glides to <paramref name="to"/>, then releases.</summary>
    public static void Drag(Point from, Point to, int steps, int stepDelayMs, int settleMs)
    {
        steps = Math.Max(2, steps);
        stepDelayMs = Math.Max(1, stepDelayMs);

        // Settle the pointer on the grab point before pressing, otherwise the app sees the press
        // and the first move in the same tick and may miss its drag threshold.
        MoveTo(from);
        Thread.Sleep(stepDelayMs * 2);
        LeftDown();
        Thread.Sleep(stepDelayMs * 2);

        for (var step = 1; step <= steps; step++)
        {
            var progress = (double)step / steps;
            MoveTo(new Point(
                from.X + (to.X - from.X) * progress,
                from.Y + (to.Y - from.Y) * progress));
            Thread.Sleep(stepDelayMs);
        }

        Thread.Sleep(stepDelayMs * 2);
        LeftUp();
        Thread.Sleep(Math.Max(0, settleMs));
    }

    public static void MoveTo(Point screenPoint)
    {
        var (x, y) = ToAbsolute(screenPoint);
        Send(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, x, y);
    }

    public static void LeftDown() => Send(MOUSEEVENTF_LEFTDOWN, 0, 0);

    public static void LeftUp() => Send(MOUSEEVENTF_LEFTUP, 0, 0);

    /// <summary>Maps a screen pixel to the 0..65535 space SendInput expects across all monitors.</summary>
    private static (int X, int Y) ToAbsolute(Point screenPoint)
    {
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN) - 1);
        var height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN) - 1);

        var x = (int)Math.Round((screenPoint.X - left) * 65535.0 / width);
        var y = (int)Math.Round((screenPoint.Y - top) * 65535.0 / height);
        return (Clamp(x), Clamp(y));
    }

    private static int Clamp(int value) => value < 0 ? 0 : value > 65535 ? 65535 : value;

    private static void Send(uint flags, int x, int y)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = flags }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
            throw new InvalidOperationException(
                $"SendInput failed (win32 error {Marshal.GetLastWin32Error()}).");
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT is a union; MOUSEINPUT is the largest member we use, so a sequential layout matches.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
