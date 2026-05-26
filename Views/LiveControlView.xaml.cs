using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class LiveControlView : UserControl
{
    private bool _isLive;
    private bool _micOn = true;
    private bool _camOn = true;

    // Preview state
    private bool _previewRunning;
    private Action<BitmapSource>? _previewHandler;

    // Audio meter state
    private Action<Dictionary<string, float>>? _audioLevelsHandler;
    private DateTime _lastMicSignalTime = DateTime.MinValue;

    // Cached meter brushes — created once, reused at 20 Hz to avoid GC pressure
    private static readonly SolidColorBrush _meterGreen = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush _meterAmber = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush _meterRed   = new(Color.FromRgb(0xEF, 0x44, 0x44));

    // Stream timer
    private readonly DispatcherTimer _streamTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _streamStartTime;

    // Fallback scenes shown before OBS connects — replaced with real OBS scenes on connect
    private static readonly string[] _fallbackScenes =
        { "Main Gameplay", "Just Chatting", "BRB Screen", "Starting Soon", "Ending Screen" };

    private string[] _scenes = _fallbackScenes;
    private string   _activeSceneName = "";

    // H5: use the app-wide singleton — never create a new instance in a view
    private static OBSWebSocketService _obs => OBSWebSocketService.Shared;

    // M5: auto-detected audio source names (populated when OBS connects)
    private string _detectedMicName    = "";
    private string _detectedOutputName = "";

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

        // M5/L3: capture first detected mic and output names for mute/volume control
        _obs.AudioInputsLoaded += ((string[] Mics, string[] Outputs) inputs) =>
            Dispatcher.Invoke(() =>
            {
                if (inputs.Mics.Length > 0)    _detectedMicName    = inputs.Mics[0];
                if (inputs.Outputs.Length > 0) _detectedOutputName = inputs.Outputs[0];
            });

        // Stream timer tick
        _streamTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _streamStartTime;
            StreamTimerText.Text = $"⏱  {(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        };

        // Try auto-connect on load (works if OBS is already open with no password)
        Loaded += async (_, _) => await TryConnectOBSAsync();

        // Audio level meters — subscribe to the ~20 Hz push event from OBS
        _audioLevelsHandler = levels =>
            Dispatcher.BeginInvoke(() => OnAudioLevelsUpdated(levels));
        _obs.AudioLevelsUpdated += _audioLevelsHandler;

        // Stop capture when navigating away — do NOT stop OBS Virtual Camera itself
        Unloaded += (_, _) =>
        {
            if (_previewHandler != null)
                _ = VirtualCameraService.Instance.StopCaptureAsync(_previewHandler);
            _previewHandler = null;
            _previewRunning = false;

            if (_audioLevelsHandler != null)
                _obs.AudioLevelsUpdated -= _audioLevelsHandler;
        };

        // Re-evaluate Pro gate when Store confirms (or revokes) entitlement
        EntitlementService.Refreshed += () => Dispatcher.Invoke(ApplyPreviewProGate);
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
                PreviewPanel.Visibility   = Visibility.Visible;
                ApplyPreviewProGate();
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
                PreviewPanel.Visibility   = Visibility.Collapsed;
                // Stop any active capture and reset button state
                if (_previewRunning) StopPreviewInternal();
                // Reset meters to inactive grey
                DesktopMeterFill.Width    = 0;
                MicMeterFill.Width        = 0;
                MicSilenceWarning.Visibility = Visibility.Collapsed;
                _lastMicSignalTime        = DateTime.MinValue;
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
        LiveBadge.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
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
        LiveIndicatorBg.Color   = Color.FromRgb(0x1A, 0x2A, 0x22);   // UI7: sage dark, not purple
        LiveDot.Foreground      = (Brush)FindResource("AccentLight");
        OfflineBanner.Visibility = Visibility.Visible;

        // Stop stream timer
        _streamTimer.Stop();
        StreamTimerText.Visibility = Visibility.Collapsed;
        StreamTimerText.Text       = "";

        // Clear silence warning — reset so it doesn't re-fire instantly on the next stream
        _lastMicSignalTime           = DateTime.MinValue;
        MicSilenceWarning.Visibility = Visibility.Collapsed;

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
            btn.Background  = new SolidColorBrush(Color.FromArgb(0x40, 0x6B, 0x9E, 0x85));   // UI7: sage tint
            btn.Foreground  = (Brush)FindResource("AccentLight");
            btn.BorderBrush = (Brush)FindResource("AccentBorder");
        }
    }

    // ── Audio sliders ────────────────────────────────────────────────────────

    // L3: update label AND push volume to OBS in real-time
    private async void DesktopSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        DesktopAudioPct.Text = $"{(int)e.NewValue}%";
        if (!string.IsNullOrEmpty(_detectedOutputName))
            await _obs.SetInputVolumeAsync(_detectedOutputName, e.NewValue / 100.0);
    }

    private async void MicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MicAudioPct.Text = $"{(int)e.NewValue}%";
        if (!string.IsNullOrEmpty(_detectedMicName))
            await _obs.SetInputVolumeAsync(_detectedMicName, e.NewValue / 100.0);
    }

    // ── Mic / Cam toggles ────────────────────────────────────────────────────

    // M5: toggle mute in OBS via the auto-detected mic source name
    private async void MicButton_Click(object sender, RoutedEventArgs e)
    {
        _micOn = !_micOn;
        MicButton.Content    = _micOn ? "🎙  Mic On" : "🔇  Mic Off";
        MicButton.Foreground = _micOn
            ? (Brush)FindResource("SecondaryText")
            : (Brush)FindResource("DangerBrush");

        // Mute = NOT _micOn (muted when mic is "off")
        if (!string.IsNullOrEmpty(_detectedMicName))
            await _obs.SetInputMuteAsync(_detectedMicName, !_micOn);
    }

    private void CamButton_Click(object sender, RoutedEventArgs e)
    {
        // Cam source is a video capture device, not an audio input — no OBS mute API needed.
        // Future: use SetSourceFilterEnabled to toggle a visibility filter on the cam source.
        _camOn = !_camOn;
        CamButton.Content    = _camOn ? "📷  Cam On" : "🚫  Cam Off";
        CamButton.Foreground = _camOn
            ? (Brush)FindResource("SecondaryText")
            : (Brush)FindResource("DangerBrush");
    }

    // ── Audio level meters ────────────────────────────────────────────────────

    private void OnAudioLevelsUpdated(Dictionary<string, float> levels)
    {
        if (levels.Count == 0) return;

        // Desktop output meter — use detected name; fall back to loudest source
        float desktopLevel = 0f;
        if (!string.IsNullOrEmpty(_detectedOutputName))
            levels.TryGetValue(_detectedOutputName, out desktopLevel);
        else
            foreach (var v in levels.Values) if (v > desktopLevel) desktopLevel = v;
        UpdateMeter(DesktopMeterTrack, DesktopMeterFill, desktopLevel);

        // Microphone meter + silence detection — use detected name; fall back to second source
        float micLevel = 0f;
        if (!string.IsNullOrEmpty(_detectedMicName))
            levels.TryGetValue(_detectedMicName, out micLevel);
        else
        {
            // No name yet — skip first key (likely desktop), use second if available
            int idx = 0;
            foreach (var kv in levels) { if (idx++ == 1) { micLevel = kv.Value; break; } }
        }
        UpdateMeter(MicMeterTrack, MicMeterFill, micLevel);

        if (micLevel > 0.01f)
            _lastMicSignalTime = DateTime.Now;

        // Silence warning: only while streaming, mic is on, and a name is known
        bool silentTooLong = _isLive
            && _micOn
            && !string.IsNullOrEmpty(_detectedMicName)
            && _lastMicSignalTime != DateTime.MinValue
            && (DateTime.Now - _lastMicSignalTime).TotalSeconds > 5;

        MicSilenceWarning.Visibility = silentTooLong ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void UpdateMeter(Border track, Border fill, float level)
    {
        var trackWidth = track.ActualWidth;
        if (trackWidth <= 0) return;

        fill.Width = Math.Clamp(level, 0f, 1f) * trackWidth;
        fill.Background = level > 0.9f ? _meterRed
                        : level > 0.6f ? _meterAmber
                        : _meterGreen;
    }

    // ── Live Preview ─────────────────────────────────────────────────────────

    private void ApplyPreviewProGate()
    {
        bool isPro = FeatureGate.Has("live-preview");
        PreviewContent.Visibility  = isPro ? Visibility.Visible  : Visibility.Collapsed;
        PreviewProGate.Visibility  = isPro ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void StartPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewRunning)
        {
            StopPreviewInternal();
            return;
        }

        PreviewBtn.IsEnabled      = false;
        PreviewError.Visibility   = Visibility.Collapsed;

        // Check actual OBS state first — IsVirtualCamActive only reflects calls
        // made through this session; the user may have started virtual cam manually.
        var alreadyRunning = await _obs.GetVirtualCamStatusAsync();
        if (!alreadyRunning)
        {
            await _obs.StartVirtualCamAsync();
            await System.Threading.Tasks.Task.Delay(1000);   // give OBS time to spin up
        }

        // Verify the Windows device is actually available
        if (!await VirtualCameraService.Instance.FindOBSCameraAsync())
        {
            PreviewError.Visibility = Visibility.Visible;
            PreviewBtn.IsEnabled    = true;
            return;
        }

        _previewHandler = frame => PreviewImage.Source = frame;
        bool started = await VirtualCameraService.Instance.StartCaptureAsync(_previewHandler);
        if (!started)
        {
            _previewHandler         = null;
            PreviewError.Visibility = Visibility.Visible;
            PreviewBtn.IsEnabled    = true;
            return;
        }

        _previewRunning      = true;
        PreviewBtn.Content   = "⏹  Stop Preview";
        PreviewBtn.IsEnabled = true;
    }

    private void StopPreviewInternal()
    {
        if (_previewHandler != null)
        {
            _ = VirtualCameraService.Instance.StopCaptureAsync(_previewHandler);
            _previewHandler = null;
        }
        _previewRunning         = false;
        PreviewImage.Source     = null;
        PreviewBtn.Content      = "👁  Start Preview";
        PreviewError.Visibility = Visibility.Collapsed;
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
