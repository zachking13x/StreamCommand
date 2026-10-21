using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StreamCommand.Services;
using TC = StreamCommand.Services.ThemeColors;

namespace StreamCommand.Views;

public partial class LiveControlView : UserControl
{
    private bool _isLive;
    private bool _micOn = true;
    private bool _camOn = true;

    // Preview state
    private bool _previewRunning;
    private Action<BitmapSource>? _previewHandler;

    // Preview stall watchdog — detects a frozen feed (OBS Virtual Camera stopped)
    private DispatcherTimer? _previewWatchdog;
    private bool _previewStalled;
    private bool _previewRestartTried;   // one auto-restart attempt per stall

    // Audio meter state
    private Action<Dictionary<string, float>>? _audioLevelsHandler;
    private DateTime _lastMicSignalTime = DateTime.MinValue;

    // Cached meter brushes — created once, reused at 20 Hz to avoid GC pressure
    // Healthy level = the current accent; looked up per update so an accent change in Settings
    // applies immediately (a static brush captured whatever accent was active at first load).
    private Brush MeterOk => (Brush)FindResource("AccentBrush");
    private static readonly SolidColorBrush _meterAmber = new(TC.Warning);
    private static readonly SolidColorBrush _meterRed   = new(TC.Danger);

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

        // OBS accepted the connection but refused an operation (review OBS-04) — show it
        // instead of letting a failed scene switch or stream start look like a no-op.
        _obs.RequestFailed += failure =>
            Dispatcher.BeginInvoke(() =>
            {
                OBSStatusText.Text = string.IsNullOrWhiteSpace(failure.Comment)
                    ? $"OBS refused '{failure.RequestType}' (code {failure.Code})."
                    : $"OBS refused '{failure.RequestType}': {failure.Comment}";
            });

        // After a reconnect, republish reconciled state to the UI.
        _obs.StateSynchronized += () =>
            Dispatcher.BeginInvoke(() => OnOBSStreamingStateChanged(_obs.IsStreaming));

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
            StreamTimerText.Text = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        };

        // Try auto-connect on load (works if OBS is already open with no password)
        Loaded += async (_, _) =>
        {
            // Seed from the CURRENT OBS state before anything else. This view is created
            // lazily, so when OBS connects at app startup the Connected event fires before
            // the view exists — it then showed its default "not connected" layout while
            // OBS was actually connected, and Connect did nothing because ConnectAsync
            // short-circuits when already connected.
            OnOBSStateChanged(_obs.State);
            UpdateHotkeyStatus();   // may have been toggled in Settings since last visit
            await TryConnectOBSAsync();
        };

        // Keep the preview 16:9 like the stream it shows (a fixed 380px height letterboxed it on
        // wide windows), capped so the scenes stay on screen.
        PreviewPanel.SizeChanged += (_, _) =>
        {
            var h = Math.Clamp((PreviewPanel.ActualWidth - 32) * 9 / 16, 300, 600);
            PreviewFrame.Height = h;
            PreviewLockedState.Height = h;
        };

        // Follow OBS for program scene + mic mute, whoever changed them (a global shortcut,
        // these buttons, or the streamer clicking in OBS).
        _obs.ProgramSceneChanged += scene => Dispatcher.BeginInvoke(() =>
        {
            _activeSceneName = scene;
            RefreshSceneButtons();
        });
        _obs.InputMuteChanged += (input, muted) => Dispatcher.BeginInvoke(() =>
        {
            if (string.IsNullOrEmpty(_detectedMicName) || input == _detectedMicName)
                SetMicUi(!muted);
        });

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
        => await TryConnectOBSAsync(showFeedback: true);

    private async System.Threading.Tasks.Task TryConnectOBSAsync(bool showFeedback = false)
    {
#if DEBUG
        if (Dev.StoreCapture.Active) return;   // demo data only — never touch a real OBS
#endif
        // Already connected: ConnectAsync would return instantly, so just resync this view.
        // Without this the button looked dead whenever the UI had drifted out of sync.
        if (_obs.State == OBSState.Connected)
        {
            OnOBSStateChanged(OBSState.Connected);
            return;
        }


        // Only narrate a connect the USER asked for. The silent auto-connect at launch must
        // not flash an error card at someone who simply doesn't have OBS open yet.
        if (showFeedback) ShowOBSFeedback("Connecting to OBS…", isError: false);

        ConnectOBSButton.IsEnabled     = false;
        ConnectOBSButtonCard.IsEnabled = false;
        // force: also starts OBS if it is closed, instead of just failing to reach it
        bool ok = await _obs.StartFromSettingsAsync(force: true);
        ConnectOBSButton.IsEnabled     = true;
        ConnectOBSButtonCard.IsEnabled = true;

        if (!showFeedback) return;

        if (ok)
        {
            OBSConnectFeedback.Visibility = Visibility.Collapsed;
            return;
        }

        // _obs.StatusMessage already distinguishes "is OBS open?" from a rejected password;
        // surface it next to the button instead of only in the side panel.
        ShowOBSFeedback(_obs.StatusMessage.Replace("OBS: ", ""), isError: true);
    }
    private void ShowOBSFeedback(string message, bool isError)
    {
        OBSConnectFeedbackText.Text = message;
        OBSConnectFeedbackText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "PrimaryText");
        OBSConnectFeedback.BorderBrush    = (Brush)FindResource(isError ? "DangerBrush" : "AccentBrush");
        OBSConnectFeedback.Visibility     = Visibility.Visible;
    }
    private void OnOBSStateChanged(OBSState state)
    {
        OBSStatusText.Text = _obs.StatusMessage;

        switch (state)
        {
            case OBSState.Connected:
                OBSStatusText.Foreground = (Brush)FindResource("SuccessBrush");
                ConnectOBSButton.Content  = "OBS connected";
                ConnectOBSButton.IsEnabled = false;
                ConnectOBSButtonCard.IsEnabled = false;
                GoLiveButton.IsEnabled    = true;
                StreamStatusText.Text     = "OBS connected — ready to go live";
                OfflineBanner.Visibility  = Visibility.Collapsed;
                OBSConnectFeedback.Visibility = Visibility.Collapsed;
                PreviewPanel.Visibility   = Visibility.Visible;
                ApplyPreviewProGate();
                StreamEvents.RaiseOBSState(true);
                break;

            case OBSState.Connecting:
                OBSStatusText.Foreground = (Brush)FindResource("MutedText");
                ConnectOBSButton.Content  = "Connecting…";
                break;

            // Reconnecting was added to the enum when auto-reconnect landed but never handled
            // here, so the switch fell through and left the button DISABLED still reading
            // "OBS connected" while the panel said otherwise — clicking it did nothing.
            case OBSState.Reconnecting:
                OBSStatusText.Foreground   = (Brush)FindResource("MutedText");
                ConnectOBSButton.Content   = "Reconnecting…";
                ConnectOBSButton.IsEnabled = true;    // let the user force a retry
                ConnectOBSButtonCard.IsEnabled = true;
                GoLiveButton.IsEnabled     = false;
                break;

            case OBSState.Error:
            case OBSState.Disconnected:
                OBSStatusText.Foreground = (Brush)FindResource("DangerBrush");
                ConnectOBSButton.Content  = "Connect OBS";
                ConnectOBSButton.IsEnabled = true;
                ConnectOBSButtonCard.IsEnabled = true;
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
        GoLiveButton.Content    = "END STREAM";
        GoLiveButton.Style      = (Style)FindResource("HeroEndStreamButton");
        StreamStatusText.Text   = "You are live — broadcasting now";
        LiveIndicatorBg.Color   = ((SolidColorBrush)FindResource("OnAirMuted")).Color;
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
        GoLiveButton.Content    = "GO LIVE";
        GoLiveButton.Style      = (Style)FindResource("HeroGoLiveButton");
        bool obsUp = _obs.State == OBSState.Connected;
        StreamStatusText.Text   = obsUp ? "OBS connected — ready to go live" : "Connect OBS to enable Go Live";
        LiveIndicatorBg.Color   = ((SolidColorBrush)FindResource("AccentMuted")).Color;
        LiveDot.Foreground      = (Brush)FindResource("AccentLight");
        // The connect guide shares the preview slot — only bring it back if OBS is actually down.
        OfflineBanner.Visibility = obsUp ? Visibility.Collapsed : Visibility.Visible;

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
        for (int i = 0; i < scenes.Length; i++)
        {
            var sceneName = scenes[i];   // capture for closure
            var btn = new Button
            {
                Content = SceneTileContent(sceneName, HotkeyBindings.SceneKey(i)),
                Tag     = sceneName,
                Margin  = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(16, 0, 12, 0),
                Height  = 56,
                FontSize = 15,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
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

    /// <summary>Scene name on the left, its global F-key on the right (blank past F8).</summary>
    private UIElement SceneTileContent(string name, string key)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (key.Length > 0)
        {
            var chip = new Border { Style = (Style)FindResource("KeyChip"), Margin = new Thickness(8, 0, 0, 0),
                                    Child = new TextBlock { Text = key, Style = (Style)FindResource("KeyChipText") } };
            Grid.SetColumn(chip, 1);
            grid.Children.Add(chip);
        }
        return grid;
    }

    private void ApplySceneStyle(Button btn, bool active)
    {
        btn.Style = (Style)FindResource("SceneTile");
        // Clear rather than set the inactive look: local values outrank the style, so a stale
        // highlight would otherwise stick to the previous scene after a switch.
        if (active)
        {
            btn.SetResourceReference(Control.BackgroundProperty,  "AccentMuted");
            btn.SetResourceReference(Control.ForegroundProperty,  "AccentLight");
            btn.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            btn.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { ShadowDepth = 0, BlurRadius = 18, Opacity = 0.45, Color = (Color)FindResource("AccentColor") };
        }
        else
        {
            btn.ClearValue(Control.BackgroundProperty);
            btn.ClearValue(Control.ForegroundProperty);
            btn.ClearValue(Control.BorderBrushProperty);
            btn.Effect = null;
        }
    }

    private void UpdateHotkeyStatus()
    {
        bool on = GlobalHotkeyService.Instance.IsActive;
        HotkeyStatusText.Text = on ? "Global shortcuts on" : "Global shortcuts off";
        HotkeyStatusText.SetResourceReference(TextBlock.ForegroundProperty, on ? "AccentLight" : "SecondaryText");
        HotkeyStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, on ? "AccentBrush" : "MutedText");
        HotkeyStatusChip.SetResourceReference(Border.BackgroundProperty, on ? "AccentMuted" : "InsetBackground");
        HotkeyStatusChip.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBorder" : "BorderLight");
    }

    private void SetMicUi(bool micOn)
    {
        _micOn = micOn;
        MicButton.Content = micOn ? "Mic on" : "Mic muted";
        MicButton.SetResourceReference(Control.ForegroundProperty, micOn ? "PrimaryText" : "DangerBrush");
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
        SetMicUi(!_micOn);

        // Mute = NOT _micOn (muted when mic is "off"); OBS echoes InputMuteStateChanged back.
        if (!string.IsNullOrEmpty(_detectedMicName))
            await _obs.SetInputMuteAsync(_detectedMicName, !_micOn);
    }

    private void CamButton_Click(object sender, RoutedEventArgs e)
    {
        // Cam source is a video capture device, not an audio input — no OBS mute API needed.
        // Future: use SetSourceFilterEnabled to toggle a visibility filter on the cam source.
        _camOn = !_camOn;
        CamButton.Content    = _camOn ? "Cam on" : "Cam off";
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

    private void UpdateMeter(Border track, Border fill, float level)
    {
        var trackWidth = track.ActualWidth;
        if (trackWidth <= 0) return;

        fill.Width = Math.Clamp(level, 0f, 1f) * trackWidth;
        fill.Background = level > 0.9f ? _meterRed
                        : level > 0.6f ? _meterAmber
                        : MeterOk;
    }

    // ── Live Preview ─────────────────────────────────────────────────────────

    private void ApplyPreviewProGate()
    {
        bool isPro = FeatureGate.Has("live-preview");
        PreviewContent.Visibility     = isPro ? Visibility.Visible  : Visibility.Collapsed;
        PreviewLockedState.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
        // The Start Preview button is meaningless for free users — hide it behind the gate.
        PreviewBtn.Visibility         = isPro ? Visibility.Visible  : Visibility.Collapsed;
    }

    private void PreviewUpgrade_Click(object sender, RoutedEventArgs e)
    {
        var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
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

        // Ensure OBS Virtual Camera is running before we try to capture from it
        var alreadyRunning = await _obs.GetVirtualCamStatusAsync();
        if (!alreadyRunning)
        {
            await _obs.StartVirtualCamAsync();
            await System.Threading.Tasks.Task.Delay(1000);   // give OBS time to spin up the DirectShow device
        }

        // Start capture — returns false if OBS Virtual Camera device isn't found
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

        StartPreviewWatchdog();
    }

    private void StopPreviewInternal()
    {
        StopPreviewWatchdog();
        if (_previewHandler != null)
        {
            _ = VirtualCameraService.Instance.StopCaptureAsync(_previewHandler);
            _previewHandler = null;
        }
        _previewRunning              = false;
        PreviewImage.Source          = null;
        PreviewBtn.Content           = "👁  Start Preview";
        PreviewError.Visibility      = Visibility.Collapsed;
        PreviewStalledOverlay.Visibility = Visibility.Collapsed;
    }

    // ── Stall watchdog ─────────────────────────────────────────────────────────

    private void StartPreviewWatchdog()
    {
        _previewStalled      = false;
        _previewRestartTried = false;
        _previewWatchdog?.Stop();
        _previewWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _previewWatchdog.Tick += PreviewWatchdog_Tick;
        _previewWatchdog.Start();
    }

    private void StopPreviewWatchdog()
    {
        if (_previewWatchdog != null)
        {
            _previewWatchdog.Stop();
            _previewWatchdog.Tick -= PreviewWatchdog_Tick;
            _previewWatchdog = null;
        }
        _previewStalled      = false;
        _previewRestartTried = false;
    }

    private async void PreviewWatchdog_Tick(object? sender, EventArgs e)
    {
        if (!_previewRunning) return;

        var svc       = VirtualCameraService.Instance;
        var lastFrame = svc.LastFrameTime;
        bool stale    = svc.IsCapturing
                     && lastFrame != DateTime.MinValue
                     && (DateTime.UtcNow - lastFrame) > TimeSpan.FromSeconds(3);

        if (stale && !_previewStalled)
        {
            // Feed just stalled — show the paused overlay instead of a frozen frame.
            _previewStalled = true;
            PreviewStalledOverlay.Visibility = Visibility.Visible;

            // Attempt exactly one automatic restart of the capture device.
            if (!_previewRestartTried)
            {
                _previewRestartTried = true;
                if (_previewHandler != null)
                {
                    await VirtualCameraService.Instance.StopCaptureAsync(_previewHandler);
                    await VirtualCameraService.Instance.StartCaptureAsync(_previewHandler);
                }
            }
        }
        else if (!stale && _previewStalled)
        {
            // Frames are flowing again — clear the overlay and re-arm the one-shot restart
            // so a future stall gets its own single recovery attempt.
            _previewStalled      = false;
            _previewRestartTried = false;
            PreviewStalledOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ── Settings navigation ──────────────────────────────────────────────────

    private void GoToOBSSettings_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("setup-guide");

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

#if DEBUG
    // ── Store screenshot demo (Debug builds only — see Dev/StoreCapture.cs) ──────
    public void ApplyStoreDemo(ImageSource gameFrame)
    {
        _streamTimer.Stop();
        OBSStatusText.Text = "OBS: Connected ✓";
        OBSStatusText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        ConnectOBSButton.Content   = "OBS connected";
        ConnectOBSButton.IsEnabled = false;
        GoLiveButton.IsEnabled     = true;
        OfflineBanner.Visibility   = Visibility.Collapsed;
        PreviewPanel.Visibility    = Visibility.Visible;
        ApplyPreviewProGate();
        PreviewImage.Source = gameFrame;
        PreviewBtn.Content  = "Stop preview";

        _isLive = true;
        SetLiveUI();
        LiveBadge.Visibility = Visibility.Visible;
        _streamTimer.Stop();
        StreamTimerText.Text = "01:42:18";

        _scenes = ["Starting Soon", "Gameplay", "Just Chatting", "Be Right Back", "Ending", "Webcam Full"];
        _activeSceneName = "Gameplay";
        BuildSceneButtons(_scenes);

        UpdateLayout();
        UpdateMeter(MicMeterTrack, MicMeterFill, 0.58f);
        UpdateMeter(DesktopMeterTrack, DesktopMeterFill, 0.72f);
    }
#endif
}
