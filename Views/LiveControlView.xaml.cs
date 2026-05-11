using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class LiveControlView : UserControl
{
    private bool _isLive;
    private bool _micOn = true;
    private bool _camOn = true;

    // Stream timer
    private readonly DispatcherTimer _streamTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _streamStartTime;

    // Fallback scenes shown before OBS connects — replaced with real OBS scenes on connect
    private static readonly string[] _fallbackScenes =
        { "Main Gameplay", "Just Chatting", "BRB Screen", "Starting Soon", "Ending Screen" };

    private string[] _scenes = _fallbackScenes;
    private string   _activeSceneName = "";

    private readonly OBSWebSocketService _obs = new();

    public LiveControlView()
    {
        InitializeComponent();
        BuildSceneButtons(_scenes);

        // Wire OBS events back to the UI thread
        _obs.StateChanged += state =>
            Dispatcher.Invoke(() => OnOBSStateChanged(state));

        _obs.StreamingStateChanged += isLive =>
            Dispatcher.Invoke(() => OnOBSStreamingStateChanged(isLive));

        // When OBS sends us the real scene list, replace the buttons
        _obs.ScenesLoaded += scenes =>
            Dispatcher.Invoke(() =>
            {
                _scenes = scenes;
                BuildSceneButtons(_scenes);
            });

        // Stream timer tick
        _streamTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _streamStartTime;
            StreamTimerText.Text = $"⏱  {(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        };

        // Try auto-connect on load (works if OBS is already open with no password)
        Loaded += async (_, _) => await TryConnectOBSAsync();
    }

    // ── OBS Connection ───────────────────────────────────────────────────────

    private async void ConnectOBS_Click(object sender, RoutedEventArgs e)
        => await TryConnectOBSAsync();

    private async System.Threading.Tasks.Task TryConnectOBSAsync()
    {
        var s = SettingsService.Load();
        ConnectOBSButton.IsEnabled = false;
        await _obs.ConnectAsync("localhost", s.OBSWebSocketPort, s.OBSWebSocketPassword);
        ConnectOBSButton.IsEnabled = true;
    }

    private void OnOBSStateChanged(OBSState state)
    {
        OBSStatusText.Text = _obs.StatusMessage;

        switch (state)
        {
            case OBSState.Connected:
                OBSStatusText.Foreground = (Brush)FindResource("SuccessBrush");
                ConnectOBSButton.Content  = "✓  OBS Connected";
                ConnectOBSButton.IsEnabled = false;
                GoLiveButton.IsEnabled    = true;
                StreamStatusText.Text     = "OBS connected — ready to go live";
                OfflineBanner.Visibility  = Visibility.Collapsed;
                StreamEvents.RaiseOBSState(true);
                break;

            case OBSState.Connecting:
                OBSStatusText.Foreground = (Brush)FindResource("MutedText");
                ConnectOBSButton.Content  = "Connecting…";
                break;

            case OBSState.Error:
            case OBSState.Disconnected:
                OBSStatusText.Foreground = (Brush)FindResource("DangerBrush");
                ConnectOBSButton.Content  = "⚡  Connect OBS";
                ConnectOBSButton.IsEnabled = true;
                GoLiveButton.IsEnabled    = false;
                OfflineBanner.Visibility  = Visibility.Visible;
                StreamEvents.RaiseOBSState(false);
                if (state == OBSState.Disconnected && _isLive)
                {
                    _isLive = false;
                    ResetLiveUI();
                }
                break;
        }
    }

    private void OnOBSStreamingStateChanged(bool isLive)
    {
        _isLive = isLive;
        if (isLive)
            SetLiveUI();
        else
            ResetLiveUI();
    }

    // ── Go Live / End Stream ─────────────────────────────────────────────────

    private async void GoLive_Click(object sender, RoutedEventArgs e)
    {
        GoLiveButton.IsEnabled = false;

        if (!_isLive)
        {
            var confirm = MessageBox.Show(
                "Ready to go live?\n\nDouble-check your scene, audio levels, and game before starting.",
                "Go Live",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                GoLiveButton.IsEnabled = true;
                return;
            }

            await _obs.StartStreamAsync();
            StreamEvents.RaiseAlert("🔴", "You are now LIVE! Good luck out there!");
        }
        else
        {
            var confirm = MessageBox.Show(
                "End your stream?",
                "End Stream",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                GoLiveButton.IsEnabled = true;
                return;
            }

            await _obs.StopStreamAsync();
            StreamEvents.RaiseAlert("⏹", "Stream ended. Great stream!");
        }

        // OBS fires StreamingStateChanged to update UI
        await System.Threading.Tasks.Task.Delay(1500);
        GoLiveButton.IsEnabled = true;
    }

    private void SetLiveUI()
    {
        GoLiveButton.Content    = "⏹   End Stream";
        GoLiveButton.Style      = (Style)FindResource("LiveButton");
        StreamStatusText.Text   = "🔴  You are LIVE — broadcasting now";
        LiveIndicatorBg.Color   = Color.FromRgb(0x7F, 0x1D, 0x1D);
        LiveDot.Foreground      = (Brush)FindResource("LiveBrush");
        OfflineBanner.Visibility = Visibility.Collapsed;

        // Start stream timer
        _streamStartTime = DateTime.Now;
        StreamTimerText.Visibility = Visibility.Visible;
        _streamTimer.Start();

        StreamEvents.RaiseStreamState(true);
    }

    private void ResetLiveUI()
    {
        GoLiveButton.Content    = "▶   Go Live";
        GoLiveButton.Style      = (Style)FindResource("PrimaryButton");
        StreamStatusText.Text   = "OBS connected — ready to go live";
        LiveIndicatorBg.Color   = Color.FromRgb(0x2E, 0x10, 0x65);
        LiveDot.Foreground      = (Brush)FindResource("AccentLight");
        OfflineBanner.Visibility = Visibility.Visible;

        // Stop stream timer
        _streamTimer.Stop();
        StreamTimerText.Visibility = Visibility.Collapsed;
        StreamTimerText.Text       = "";

        StreamEvents.RaiseStreamState(false);
    }

    // ── Scene buttons ────────────────────────────────────────────────────────

    private void BuildSceneButtons(string[] scenes)
    {
        ScenePanel.Children.Clear();
        foreach (var name in scenes)
        {
            var sceneName = name;   // capture for closure
            var btn = new Button
            {
                Content = sceneName,
                Tag     = sceneName,
                Margin  = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(14, 10, 14, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor  = System.Windows.Input.Cursors.Hand
            };
            ApplySceneStyle(btn, sceneName == _activeSceneName);
            btn.Click += async (_, _) =>
            {
                _activeSceneName = sceneName;
                RefreshSceneButtons();
                // Send the scene change to OBS (no-op if not connected)
                await _obs.SetSceneAsync(sceneName);
            };
            ScenePanel.Children.Add(btn);
        }
    }

    private void RefreshSceneButtons()
    {
        foreach (var child in ScenePanel.Children)
            if (child is Button b && b.Tag is string tag)
                ApplySceneStyle(b, tag == _activeSceneName);
    }

    private void ApplySceneStyle(Button btn, bool active)
    {
        btn.Style = (Style)FindResource("SecondaryButton");
        if (active)
        {
            btn.Background  = new SolidColorBrush(Color.FromArgb(0x40, 0x7C, 0x3A, 0xED));
            btn.Foreground  = (Brush)FindResource("AccentLight");
            btn.BorderBrush = (Brush)FindResource("AccentBorder");
        }
    }

    // ── Audio sliders ────────────────────────────────────────────────────────

    private void DesktopSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => DesktopAudioPct.Text = $"{(int)e.NewValue}%";

    private void MicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => MicAudioPct.Text = $"{(int)e.NewValue}%";

    // ── Mic / Cam toggles ────────────────────────────────────────────────────

    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        _micOn = !_micOn;
        MicButton.Content    = _micOn ? "🎙  Mic On" : "🔇  Mic Off";
        MicButton.Foreground = _micOn
            ? (Brush)FindResource("SecondaryText")
            : (Brush)FindResource("DangerBrush");
    }

    private void CamButton_Click(object sender, RoutedEventArgs e)
    {
        _camOn = !_camOn;
        CamButton.Content    = _camOn ? "📷  Cam On" : "🚫  Cam Off";
        CamButton.Foreground = _camOn
            ? (Brush)FindResource("SecondaryText")
            : (Brush)FindResource("DangerBrush");
    }

    // ── Settings navigation ──────────────────────────────────────────────────

    private void GoToOBSSettings_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("settings");

    // ── Launch streaming app ─────────────────────────────────────────────────

    private void LaunchApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string appKey) return;
        var (success, msg) = AppLaunchService.TryLaunch(appKey);
        LaunchMessageText.Text = success
            ? $"✓  {btn.Content} launched successfully."
            : $"⚠  {msg}";
        LaunchMessageBorder.Visibility = Visibility.Visible;
    }
}
