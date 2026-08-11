using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AvaloniaSampleApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        // Zero-edit path: UiPilot is injected via DOTNET_STARTUP_HOOKS from UiPilot.Cli.
        // Optional in-app opt-in: UiPilot.Avalonia.PilotHost.Start();

        base.OnFrameworkInitializationCompleted();
    }
}
