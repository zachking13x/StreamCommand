using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class SetupWizard : Window
{
    private int _step = 1;
    private const int TotalSteps = 5;
    private readonly OBSWebSocketService _obsTest = new();

    // Holds the result of a successful OAuth flow so it can be persisted at Finish
    private TwitchOAuthResult?          _twitchAuth;
    private CancellationTokenSource?    _twitchCts;

    // M7: track whether OBS test passed so SaveAndBuildSummary can reflect it accurately
    private bool _obsTestPassed;

    public SetupWizard()
    {
        InitializeComponent();
        _obsTest.StateChanged += state => Dispatcher.Invoke(() => OnOBSTestResult(state));
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < TotalSteps)
            GoToStep(_step + 1);
        else
            Finish();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 1) GoToStep(_step - 1);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_step < TotalSteps)
            GoToStep(_step + 1);
        else
            Finish();
    }

    private void GoToStep(int step)
    {
        _step = step;

        Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = step is 2 or 3 or 4 ? Visibility.Visible : Visibility.Collapsed;

        NextButton.Content = step switch
        {
            1           => "Let's Go  →",
            TotalSteps  => "Open Stream Command  →",
            _           => "Next  →"
        };

        if (step == TotalSteps)
        {
            SaveAndBuildSummary();
            SkipButton.Visibility = Visibility.Collapsed;
        }

        UpdateDots();
    }

    private void UpdateDots()
    {
        var accent = (SolidColorBrush)FindResource("AccentBrush");
        var done   = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        var idle   = (SolidColorBrush)FindResource("BorderBrush");

        SetDot(Dot1, Dot1.Child as TextBlock, 1, accent, done, idle);
        SetDot(Dot2, Dot2Text, 2, accent, done, idle);
        SetDot(Dot3, Dot3Text, 3, accent, done, idle);
        SetDot(Dot4, Dot4Text, 4, accent, done, idle);
        SetDot(Dot5, Dot5Text, 5, accent, done, idle);

        Line2.Fill = _step > 2 ? done : idle;
        Line3.Fill = _step > 3 ? done : idle;
        Line4.Fill = _step > 4 ? done : idle;
    }

    private void SetDot(Border dot, TextBlock? text, int dotStep, Brush accent, Brush done, Brush idle)
    {
        if (_step == dotStep)
        {
            dot.Background = accent;
            if (text != null) { text.Foreground = Brushes.White; text.Text = dotStep.ToString(); }
        }
        else if (_step > dotStep)
        {
            dot.Background = done;
            if (text != null) { text.Foreground = Brushes.White; text.Text = "✓"; }
        }
        else
        {
            dot.Background = idle;
            if (text != null) { text.Foreground = (Brush)FindResource("MutedText"); text.Text = dotStep.ToString(); }
        }
    }

    // ── Save + Summary ───────────────────────────────────────────────────────

    private void SaveAndBuildSummary()
    {
        var s = SettingsService.Load();

        // Twitch (step 2) — already written by ConnectTwitch_Click; apply again in
        // case the user connected and then navigated forward/backward.
        if (_twitchAuth != null)
        {
            s.TwitchUsername     = _twitchAuth.Username;
            s.TwitchChatToken    = _twitchAuth.AccessToken;
            s.TwitchRefreshToken = _twitchAuth.RefreshToken;   // PKCE refresh token
            s.TwitchClientId     = _twitchAuth.ClientId;
        }

        // OBS (step 3) — SECURITY 1: validate port range before accepting
        if (OBSPasswordBox.Password.Length > 0)
            s.OBSWebSocketPassword = OBSPasswordBox.Password;
        if (int.TryParse(OBSPortBox.Text, out var port) && port >= 1024 && port <= 65535)
            s.OBSWebSocketPort = port;
        // else: silently keep the default 4455 — invalid ports are rejected

        // YouTube (step 4)
        if (!string.IsNullOrWhiteSpace(YoutubeChannelBox.Text))
            s.YoutubeChannelId = YoutubeChannelBox.Text.Trim();
        if (YoutubeApiKeyBox.Password.Length > 0)
            s.YoutubeApiKey = YoutubeApiKeyBox.Password;

        s.SetupComplete = true;
        SettingsService.Save(s);

        // Check if nothing was connected — show warning
        bool hasTwitch  = !string.IsNullOrWhiteSpace(s.TwitchUsername) &&
                          !string.IsNullOrWhiteSpace(s.TwitchChatToken);
        bool hasOBS     = _obsTestPassed;   // M7: only true when the test actually succeeded
        bool hasYoutube = !string.IsNullOrWhiteSpace(s.YoutubeChannelId);

        NoConnectionWarning.Visibility = (!hasTwitch && !hasOBS && !hasYoutube)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Build a human-friendly summary
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(s.TwitchUsername))  parts.Add($"Twitch: @{s.TwitchUsername} ✓");
        else if (!string.IsNullOrWhiteSpace(s.TwitchChatToken)) parts.Add("Twitch chat connected");
        if (!string.IsNullOrWhiteSpace(s.YoutubeChannelId))       parts.Add($"YouTube: {s.YoutubeChannelId[..Math.Min(12, s.YoutubeChannelId.Length)]}…");
        if (_obsTestPassed)
            parts.Add($"OBS WebSocket on port {s.OBSWebSocketPort} ✓");

        SetupSummary.Text = parts.Count > 0
            ? string.Join("  ·  ", parts) + "\n\nYou can update anything in Settings at any time."
            : "You skipped setup — no worries!\nAdd your credentials in Settings at any time.";
    }

    private void Finish()
    {
        SaveAndBuildSummary();
        DialogResult = true;
        Close();
    }

    // ── OBS Test ─────────────────────────────────────────────────────────────

    private async void TestOBS_Click(object sender, RoutedEventArgs e)
    {
        TestOBSButton.IsEnabled = false;
        OBSTestResult.Text = "Testing…";
        OBSTestResult.Foreground = (Brush)FindResource("MutedText");

        int.TryParse(OBSPortBox.Text, out var port);
        if (port == 0) port = 4455;

        await _obsTest.ConnectAsync("localhost", port, OBSPasswordBox.Password);
        TestOBSButton.IsEnabled = true;
    }

    private void OnOBSTestResult(OBSState state)
    {
        switch (state)
        {
            case OBSState.Connected:
                OBSTestResult.Text       = "✓  OBS connected successfully!";
                OBSTestResult.Foreground = (Brush)FindResource("SuccessBrush");
                _obsTestPassed = true;   // M7
                _ = _obsTest.DisconnectAsync();
                break;
            case OBSState.Error:
                OBSTestResult.Text       = "✗  " + _obsTest.StatusMessage.Replace("OBS: ", "");
                OBSTestResult.Foreground = (Brush)FindResource("DangerBrush");
                break;
        }
    }

    // ── Twitch OAuth ─────────────────────────────────────────────────────────

    private async void ConnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        ConnectTwitchBtn.IsEnabled = false;
        DeviceCodePanel.Visibility = Visibility.Collapsed;
        ConnectTwitchBtn.Content   = "Starting…";

        _twitchCts?.Cancel();
        _twitchCts = new CancellationTokenSource();

        try
        {
            var result = await TwitchOAuthService.AuthorizeAsync(
                onCodeReady: (userCode, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        DeviceCodeLabel.Text       = userCode;
                        DeviceCodePanel.Visibility = Visibility.Visible;
                        ConnectTwitchBtn.Content   = "Connect with Twitch";
                    });
                },
                cancellationToken: _twitchCts.Token);

            DeviceCodePanel.Visibility = Visibility.Collapsed;

            if (result != null)
            {
                _twitchAuth = result;

                var s = SettingsService.Load();
                s.TwitchUsername     = result.Username;
                s.TwitchChatToken    = result.AccessToken;
                s.TwitchRefreshToken = result.RefreshToken;
                s.TwitchClientId     = result.ClientId;
                SettingsService.Save(s);

                TwitchConnectedText.Text         = $"Connected as @{result.Username}";
                TwitchConnectedBanner.Visibility = Visibility.Visible;
                ConnectTwitchBtn.Content         = "✓  Reconnect Twitch";
            }
            else
            {
                var failure = TwitchOAuthService.LastFailure;
                ConnectTwitchBtn.Content = (failure?.Step == "Cancelled")
                    ? "Connect with Twitch"
                    : $"Failed: {failure?.Step ?? "Unknown"} — try again";
            }
        }
        catch (Exception ex)
        {
            DeviceCodePanel.Visibility = Visibility.Collapsed;
            ConnectTwitchBtn.Content   = $"Error — try again ({ex.Message[..Math.Min(40, ex.Message.Length)]})";
        }
        finally
        {
            ConnectTwitchBtn.IsEnabled = true;
        }
    }

    private void CancelTwitch_Click(object sender, RoutedEventArgs e)
    {
        _twitchCts?.Cancel();
        DeviceCodePanel.Visibility = Visibility.Collapsed;
        ConnectTwitchBtn.Content   = "Connect with Twitch";
    }
}
