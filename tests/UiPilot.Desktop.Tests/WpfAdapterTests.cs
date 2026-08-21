using System.Windows;
using System.Windows.Controls;
using UiPilot.Wpf.Interaction;
using UiPilot.Wpf.Media;
using Xunit;

namespace UiPilot.Desktop.Tests;

public sealed class WpfAdapterTests
{
    [WindowsFact]
    public void Screenshot_FreezeAndCapture_ProducesPng()
    {
        Sta.Run(() =>
        {
            var window = new Window
            {
                Width = 180,
                Height = 120,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Content = new TextBlock { Text = "shot" },
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                var shot = Screenshot.Capture(window);
                Assert.NotNull(shot);
                Assert.True(shot!.Width > 0);
                Assert.True(shot.Height > 0);
                Assert.False(string.IsNullOrEmpty(shot.Base64));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [WindowsFact]
    public void Scroll_AppliesHorizontalAxisEvenWhenVerticalIsSet()
    {
        Sta.Run(() =>
        {
            var viewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                Content = new Border { Width = 400, Height = 400 },
            };
            var window = new Window
            {
                Width = 120,
                Height = 120,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Content = viewer,
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                viewer.ScrollToHorizontalOffset(40);
                window.UpdateLayout();
                var afterDirect = viewer.HorizontalOffset;
                viewer.ScrollToHorizontalOffset(0);
                window.UpdateLayout();
                var method = SyntheticInput.Scroll(viewer, dx: 3, dy: 1);
                window.UpdateLayout();
                Assert.Equal("synthetic:scroll", method);
                Assert.True(
                    afterDirect > 0 && viewer.HorizontalOffset > 0,
                    $"direct={afterDirect} afterScroll={viewer.HorizontalOffset} extent={viewer.ExtentWidth} viewport={viewer.ViewportWidth}");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
