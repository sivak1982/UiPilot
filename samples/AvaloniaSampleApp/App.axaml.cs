using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UiPilot.Avalonia;

namespace AvaloniaSampleApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        // The only line an Avalonia app needs — same MCP protocol as WPF.
        PilotHost.Start();

        base.OnFrameworkInitializationCompleted();
    }
}
