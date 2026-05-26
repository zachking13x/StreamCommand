using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class SettingsView : UserControl
{
    private AppSettings _settings;
    private CancellationTokenSource? _twitchCts;

    public SettingsView()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        LoadIntoFields();
    }

    private void LoadIntoFields()
    {
        TwitchUsername.Text   = _settings.TwitchUsername;
        YoutubeChannelId.Text = _settings.YoutubeChannelId;
        DiscordInvite.Text    = _settings.DiscordInvite;
        OBSPort.Text          = _settings.OBSWebSocketPort.ToString();

        // Highlight whichever swatch matches the saved accent colour
        UpdateSwatchRing(_settings.AccentColor);

        // Pro status banner
        UpdateProStatus();

        // PasswordBoxes can't display existing values — show a saved-indicator instead
        // so users know the field is already populated and they only need to type if changing it.
        YoutubeApiKeySaved.Visibility = !string.IsNullOrEmpty(_settings.YoutubeApiKey)        ? Visibility.Visible : Visibility.Collapsed;
        SeTokenSaved.Visibility       = !string.IsNullOrEmpty(_settings.StreamElementsToken)  ? Visibility.Visible : Visibility.Collapsed;
        OBSPasswordSaved.Visibility   = !string.IsNullOrEmpty(_settings.OBSWebSocketPassword) ? Visibility.Visible : Visibility.Collapsed;

        // Twitch connection status
        if (!string.IsNullOrEmpty(_settings.TwitchUsername) && !string.IsNullOrEmpty(_settings.TwitchChatToken))
        {
            TwitchConnectedText.Text       = $"Connected as @{_settings.TwitchUsername}";
            TwitchConnectedBanner.Visibility = Visibility.Visible;
            ConnectTwitchBtn.Content       = BuildConnectBtnContent("Reconnect Twitch");
        }
        else
        {
            TwitchConnectedBanner.Visibility = Visibility.Collapsed;
            ConnectTwitchBtn.Content         = BuildConnectBtnContent("Connect with Twitch");
        }
    }

    private static object BuildConnectBtnContent(string label)
    {
        var panel = new System.Windows.Controls.StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = "🟣", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.TwitchUsername   = TwitchUsername.Text.Trim();
        _settings.YoutubeChannelId = YoutubeChannelId.Text.Trim();
        _settings.DiscordInvite    = DiscordInvite.Text.Trim();

        // Only overwrite a secret if the user actually typed something — otherwise
        // keep the previously saved value.  PasswordBoxes are blank by design and
        // "blank" does NOT mean "the user wants to clear the credential".
        if (YoutubeApiKey.Password.Length > 0) _settings.YoutubeApiKey        = YoutubeApiKey.Password;
        if (SeToken.Password.Length > 0)        _settings.StreamElementsToken  = SeToken.Password;
        if (OBSPassword.Password.Length > 0)    _settings.OBSWebSocketPassword = OBSPassword.Password;

        // SECURITY 1: only accept ports in the user-accessible range
        if (int.TryParse(OBSPort.Text, out var port) && port >= 1024 && port <= 65535)
            _settings.OBSWebSocketPort = port;
        else
            OBSPort.Text = _settings.OBSWebSocketPort.ToString();   // restore valid value

        SettingsService.Save(_settings);
        LoadIntoFields();   // refresh saved-indicators and Twitch banner after save

        SavedBanner.Visibility = Visibility.Visible;
        SaveBtn.Content = "✓  Saved!";
        await Task.Delay(2500);
        SavedBanner.Visibility = Visibility.Collapsed;
        SaveBtn.Content = "💾  Save Settings";
    }

    // ── Twitch OAuth (Device Code flow) ──────────────────────────────────────

    private async void ConnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        // Reset UI
        ConnectTwitchBtn.IsEnabled   = false;
        TwitchErrorBanner.Visibility = Visibility.Collapsed;
        DeviceCodePanel.Visibility   = Visibility.Collapsed;
        ConnectTwitchBtn.Content     = "Starting…";

        _twitchCts?.Cancel();
        _twitchCts = new CancellationTokenSource();

        try
        {
            var result = await TwitchOAuthService.AuthorizeAsync(
                onCodeReady: (userCode, verificationUri) =>
                {
                    // Fires on the thread-pool — marshal back to UI thread
                    Dispatcher.Invoke(() =>
                    {
                        DeviceCodeLabel.Text       = userCode;
                        DeviceCodePanel.Visibility = Visibility.Visible;
                        ConnectTwitchBtn.Content   = BuildConnectBtnContent("Connect with Twitch");
                    });
                },
                cancellationToken: _twitchCts.Token);

            DeviceCodePanel.Visibility = Visibility.Collapsed;

            if (result != null)
            {
                _settings.TwitchUsername     = result.Username;
                _settings.TwitchChatToken    = result.AccessToken;
                _settings.TwitchRefreshToken = result.RefreshToken;
                _settings.TwitchClientId     = result.ClientId;
                SettingsService.Save(_settings);

                TwitchUsername.Text              = result.Username;
                TwitchConnectedText.Text         = $"Connected as @{result.Username}";
                TwitchConnectedBanner.Visibility = Visibility.Visible;
                TwitchErrorBanner.Visibility     = Visibility.Collapsed;
                ConnectTwitchBtn.Content         = BuildConnectBtnContent("Reconnect Twitch");

                SavedBanner.Visibility = Visibility.Visible;
                await Task.Delay(2500);
                SavedBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                ConnectTwitchBtn.Content = BuildConnectBtnContent("Try again");

                var failure = TwitchOAuthService.LastFailure;
                if (failure != null && failure.Step != "Cancelled")
                {
                    TwitchErrorStep.Text         = $"⚠  Failed at: {failure.Step}";
                    TwitchErrorDetail.Text       = failure.Detail;
                    TwitchErrorBanner.Visibility = Visibility.Visible;
                }
            }
        }
        catch (Exception ex)
        {
            DeviceCodePanel.Visibility   = Visibility.Collapsed;
            ConnectTwitchBtn.Content     = BuildConnectBtnContent("Try again");
            TwitchErrorStep.Text         = "⚠  Unexpected error";
            TwitchErrorDetail.Text       = ex.Message;
            TwitchErrorBanner.Visibility = Visibility.Visible;
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
        ConnectTwitchBtn.Content   = BuildConnectBtnContent("Connect with Twitch");
    }

    // ── Accent colour picker ─────────────────────────────────────────────────

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border swatch) return;
        var hex = swatch.Tag?.ToString() ?? "#6B9E85";

        ThemeService.Apply(hex);

        _settings.AccentColor = hex;
        SettingsService.Save(_settings);

        UpdateSwatchRing(hex);
    }

    /// <summary>Puts the white selection ring on the active swatch and removes it from the rest.</summary>
    private void UpdateSwatchRing(string? activeHex)
    {
        activeHex = (activeHex ?? "#6B9E85").ToUpperInvariant();
        foreach (var swatch in new[] { Swatch1, Swatch2, Swatch3, Swatch4, Swatch5 })
        {
            var tag = swatch.Tag?.ToString()?.ToUpperInvariant() ?? "";
            var selected = tag == activeHex;
            swatch.BorderBrush     = selected ? Brushes.White : Brushes.Transparent;
            swatch.BorderThickness = selected ? new Thickness(2) : new Thickness(0);
        }
    }

    // ── Pro status + unlock code ─────────────────────────────────────────────

    private void UpdateProStatus()
    {
        bool isPro = EntitlementService.IsPro || _settings.DevProUnlock;

        if (isPro)
        {
            ProStatusText.Text            = "✦  Pro — all features unlocked";
            ProStatusText.Foreground      = (System.Windows.Media.Brush)FindResource("AccentLight");
            ProStatusBanner.Background    = (System.Windows.Media.Brush)FindResource("AccentMuted");
            ProStatusBanner.BorderBrush   = (System.Windows.Media.Brush)FindResource("AccentBorder");
            ProStatusBanner.BorderThickness = new Thickness(1);
            // Hide unlock panel — already activated
            UnlockPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ProStatusText.Text            = "○  Free tier";
            ProStatusText.Foreground      = (System.Windows.Media.Brush)FindResource("MutedText");
            ProStatusBanner.Background    = (System.Windows.Media.Brush)FindResource("CardBackground");
            ProStatusBanner.BorderBrush   = (System.Windows.Media.Brush)FindResource("BorderBrush");
            ProStatusBanner.BorderThickness = new Thickness(1);
            UnlockPanel.Visibility = Visibility.Visible;
        }
    }

    private void ActivateCode_Click(object sender, RoutedEventArgs e)
    {
        var code = UnlockCodeBox.Text.Trim();

        // SHA-256 of "STREAM-590453723"
        const string ValidHash = "B0F5F3ABC9CFD9DB3F90C83DA89EEAAEE560C2E0D92511AE77DCD6DB6A278C05";

        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = BitConverter.ToString(
            sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(code)))
            .Replace("-", "").ToUpperInvariant();

        if (hash == ValidHash)
        {
            _settings.DevProUnlock = true;
            SettingsService.Save(_settings);

            // Immediately grant Pro without waiting for next app launch
            EntitlementService.IsPro           = true;
            EntitlementService.ActiveProductId = "dev_unlock";
            EntitlementService.RaiseRefreshed();      // let views re-evaluate gates

            UnlockCodeBox.Text            = "";
            UnlockResultText.Text         = "✓  Pro unlocked on this device.";
            UnlockResultText.Foreground   = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            UnlockResultText.Visibility   = Visibility.Visible;
            UpdateProStatus();
        }
        else
        {
            UnlockResultText.Text         = "✗  Invalid code.";
            UnlockResultText.Foreground   = (System.Windows.Media.Brush)FindResource("DangerBrush");
            UnlockResultText.Visibility   = Visibility.Visible;
        }
    }

    // ── External links ───────────────────────────────────────────────────────

    private void OpenGoogleCloud_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://console.cloud.google.com/apis/library/youtube.googleapis.com");

    private void OpenSE_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://streamelements.com/dashboard/account/channels");
}
