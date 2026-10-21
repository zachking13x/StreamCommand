using System;
using System.Threading;
using System.Windows;
using StreamCommand.Services;

namespace StreamCommand;

public partial class App : Application
{
    // Single-instance guard. Nothing stopped a second copy opening, so launching the app
    // again (or starting a loose build next to the Store one) gave two identical windows
    // and no way to tell which was which — two chat connections, two sets of global
    // hotkeys, two OBS clients. The second copy now just raises the first and exits.
    private const string InstanceMutexName = @"Local\StreamCommand.SingleInstance";
    private const string ActivateEventName = @"Local\StreamCommand.Activate";

    private Mutex?            _instanceMutex;
    private EventWaitHandle?  _activateSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Hand focus to the copy that's already running, then get out of the way.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { /* the other instance is gone or starting — nothing useful to do */ }

            Shutdown();
            return;
        }

#if DEBUG
        // Debug-only: generate Microsoft Store screenshots + mockups (see Dev/StoreCapture.cs).
        bool storeCapture = Dev.StoreCapture.TryBegin(e.Args);
#endif

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

        // Exit when the main window closes. OnLastWindowClose never fired: the always-on-top
        // overlays (alerts, hotkey toast) are windows too, so closing the app left a headless
        // process running — and its hotkeys still registered.
        MainWindow   = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        mainWindow.Show();

        StartActivationListener(mainWindow);

#if DEBUG
        if (storeCapture)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(async () => await Dev.StoreCapture.RunAsync(mainWindow)));
#endif

        // Stream alert overlay — always on top, transparent, fires on StreamEvents.AlertFired
        var overlay = new Views.StreamAlertOverlay();
        overlay.Show();
    }

    /// <summary>
    /// <summary>
    /// Waits for a second launch to signal us and surfaces the existing window instead.
    ///
    /// Deliberately gentle: it will NOT drag the window over a full-screen app. Forcing focus
    /// (the Topmost on/off trick) yanks an exclusive-full-screen game to the desktop, which is
    /// catastrophic mid-stream — the whole point of this app is that you never leave the game.
    /// </summary>
    private void StartActivationListener(Window mainWindow)
    {
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        var listener = new Thread(() =>
        {
            while (true)
            {
                try { if (!_activateSignal.WaitOne()) continue; }
                catch { return; }   // handle disposed during shutdown

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Someone is gaming full-screen — leave them alone. The second instance has
                    // already exited, so nothing is lost by not raising the window.
                    if (StreamCommand.Services.ForegroundWindow.IsOtherAppFullScreen()) return;

                    if (mainWindow.WindowState == WindowState.Minimized)
                        mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Show();
                    mainWindow.Activate();   // no Topmost hack — never steal focus by force
                }));
            }
        })
        { IsBackground = true, Name = "StreamCommand.ActivationListener" };

        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _activateSignal?.Dispose(); } catch { }
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        try { _instanceMutex?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
