using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaPilot;

namespace AvaloniaSampleApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        // Same adoption shape as WpfPilot — one Start() call after the app exists.
        AvaloniaPilotHost.Start();

        base.OnFrameworkInitializationCompleted();
    }
}
