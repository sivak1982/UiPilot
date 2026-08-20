using System;
using System.Drawing;
using System.Windows.Forms;
using UiPilot.WinForms.Inspection;

namespace UiPilot.WinForms.Interaction;

internal static class HighlightOverlay
{
    public static bool Highlight(object obj, int durationMs)
    {
        var bounds = ControlTree.ScreenBounds(obj);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        var overlay = new OverlayForm(bounds);
        overlay.Show();
        var timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(1, Math.Min(60_000, durationMs)),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            overlay.Close();
            overlay.Dispose();
        };
        timer.Start();
        return true;
    }

    private sealed class OverlayForm : Form
    {
        private const int WsExTransparent = 0x20;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x80;

        public OverlayForm(Rectangle bounds)
        {
            Bounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Red;
            Opacity = 0.22;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExTransparent | WsExNoActivate | WsExToolWindow;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var pen = new Pen(Color.Red, 3);
            e.Graphics.DrawRectangle(pen, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        }
    }
}
