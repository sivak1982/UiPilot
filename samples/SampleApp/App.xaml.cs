using System.Windows;

namespace SampleApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The only line an app needs. Enabled in Debug / via WPFPILOT_ENABLE=1; a no-op otherwise.
        WpfPilot.WpfPilotHost.Start();
    }
}
