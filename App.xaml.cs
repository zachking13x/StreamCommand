using System.Windows;
using StreamCommand.Services;

namespace StreamCommand;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = SettingsService.Load();

        if (!settings.SetupComplete)
        {
            var wizard = new Views.SetupWizard();
            // ShowDialog blocks until the wizard closes.
            // If the user closes it via the X, we still launch the app.
            wizard.ShowDialog();
        }

        var mainWindow = new Views.MainWindow();
        mainWindow.Show();
    }
}
