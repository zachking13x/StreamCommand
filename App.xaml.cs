using System.Windows;
using StreamCommand.Services;

namespace StreamCommand;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent WPF from shutting down when the setup wizard closes.
        // With the default OnLastWindowClose mode, closing the wizard (the only
        // open window at that point) triggers Application.Shutdown() before
        // MainWindow is ever created — causing a silent crash on fresh installs.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = SettingsService.Load();

        // Apply saved accent colour before any window renders
        ThemeService.Apply(settings.AccentColor);

        if (!settings.SetupComplete)
        {
            var wizard = new Views.SetupWizard();
            // ShowDialog blocks until the wizard closes.
            // If the user closes it via the X, we still launch the app.
            wizard.ShowDialog();
        }

        var mainWindow = new Views.MainWindow();

        // Restore normal shutdown behaviour before showing MainWindow so the
        // app exits naturally when the user closes the main window.
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        mainWindow.Show();

        // Stream alert overlay — always on top, transparent, fires on StreamEvents.AlertFired
        var overlay = new Views.StreamAlertOverlay();
        overlay.Show();
    }
}
