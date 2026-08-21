using System.Windows.Forms;
using UiPilot.Tools;
using UiPilot.WinForms.Interaction;
using Xunit;

namespace UiPilot.Desktop.Tests;

public sealed class WinFormsAdapterTests
{
    [WindowsFact]
    public void TypeText_RejectsNonTextControls()
    {
        Sta.Run(() =>
        {
            using var button = new Button { Text = "Nope" };
            var ex = Assert.Throws<PilotToolException>(() => SyntheticInput.TypeText(button, "x"));
            Assert.Equal(PilotErrorCodes.Unsupported, ex.Code);
        });
    }

    [WindowsFact]
    public void Scroll_SendsBothAxesWithoutThrowing()
    {
        Sta.Run(() =>
        {
            using var panel = new Panel { Width = 80, Height = 80 };
            using var form = new Form
            {
                Width = 120,
                Height = 120,
                ShowInTaskbar = false,
            };
            form.Controls.Add(panel);
            form.Show();
            try
            {
                var method = SyntheticInput.Scroll(panel, dx: 1, dy: -1);
                Assert.Equal("synthetic:scroll", method);
            }
            finally
            {
                form.Close();
            }
        });
    }
}
