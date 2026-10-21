using System;
using System.Security.Cryptography;
using System.Text;
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

#if DEBUG
        // Wire the dev unlock button only in DEBUG. The XAML carries no Click attribute,
        // so in release the handler method is compiled out and nothing references it.
        ActivateCodeBtn.Click += ActivateCode_Click;
#endif
    }

    private void LoadIntoFields()
    {
        TwitchUsername.Text   = _settings.TwitchUsername;
        YoutubeChannelId.Text = _settings.YoutubeChannelId;
        DiscordInvite.Text    = _settings.DiscordInvite;
        OBSPort.Text          = _settings.OBSWebSocketPort.ToString();

        // Highlight whichever swatch matches the saved accent colour
        UpdateSwatchRing(ThemeService.CurrentAccent);

        _loadingHotkeys = true;
        HotkeysToggle.IsChecked = _settings.GlobalHotkeysEnabled;
        HotkeyModifierBox.SelectedItem = HotkeyModifierBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == HotkeyBindings.Parse(_settings.HotkeyModifier).ToString());
        _loadingHotkeys = false;
        ShowHotkeyConflicts();

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

        CredentialProtection.ClearLastError();
        SettingsService.Save(_settings);
        LoadIntoFields();   // refresh saved-indicators and Twitch banner after save

        // Apply a changed OBS port or password now — saving used to leave the old
        // connection up, so the new credentials only took effect on the next launch.
        _ = OBSWebSocketService.Shared.StartFromSettingsAsync(force: true);

        // Fail-closed reporting (audit SC-02): if encryption was unavailable the secret was
        // dropped rather than written in the clear — say so instead of claiming "Saved!".
        if (CredentialProtection.LastError != null)
        {
            CredentialWarningText.Text       = CredentialProtection.LastError;
            CredentialWarningBanner.Visibility = Visibility.Visible;
            SaveBtn.Content = "⚠  Saved without credentials";
            await Task.Delay(4000);
            SaveBtn.Content = "Save Settings";
            return;
        }

        CredentialWarningBanner.Visibility = Visibility.Collapsed;
        SavedBanner.Visibility = Visibility.Visible;
        SaveBtn.Content = "✓  Saved!";
        await Task.Delay(2500);
        SavedBanner.Visibility = Visibility.Collapsed;
        SaveBtn.Content = "Save Settings";
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
                _ = TwitchChatService.Shared.StartFromSettingsAsync();

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

    // ── Global shortcuts ─────────────────────────────────────────────────────

    private bool _loadingHotkeys;

    private void HotkeysToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingHotkeys) return;
        bool on = HotkeysToggle.IsChecked == true;

        // Saved immediately (like the accent swatches) so a later "Save Settings" can't revert it.
        _settings.GlobalHotkeysEnabled = on;
        SettingsService.Save(_settings);

        if (on) GlobalHotkeyService.Instance.Enable(HotkeyBindings.Parse(_settings.HotkeyModifier));
        else    GlobalHotkeyService.Instance.Disable();
        ShowHotkeyConflicts();
    }

    private void HotkeyModifierBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingHotkeys || HotkeyModifierBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        _settings.HotkeyModifier = tag;
        SettingsService.Save(_settings);
        if (_settings.GlobalHotkeysEnabled)
            GlobalHotkeyService.Instance.Enable(HotkeyBindings.Parse(tag));
        ShowHotkeyConflicts();
    }

    private void ShowHotkeyConflicts()
    {
        var taken = GlobalHotkeyService.Instance.Conflicts;
        HotkeyConflictText.Visibility = taken.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HotkeyConflictText.Text = taken.Count > 0
            ? $"Already used by another app, so not active: {string.Join(", ", taken.Select(b => b.Label))}. Pick different modifier keys above, or free them in that app."
            : "";
    }

    // ── Accent colour picker ─────────────────────────────────────────────────

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border swatch) return;
        var hex = swatch.Tag?.ToString() ?? ThemeService.DefaultAccent;

        ThemeService.Apply(hex);

        _settings.AccentColor = hex;
        SettingsService.Save(_settings);

        UpdateSwatchRing(hex);
    }

    /// <summary>Puts the white selection ring on the active swatch and removes it from the rest.</summary>
    private void UpdateSwatchRing(string? activeHex)
    {
        activeHex = ThemeService.Normalize(activeHex ?? ThemeService.DefaultAccent);
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
#if DEBUG
        bool isPro = EntitlementService.IsPro || _settings.DevProUnlock;
#else
        // Release: DevProUnlock is ignored entirely (see EntitlementService.RefreshAsync).
        bool isPro = EntitlementService.IsPro;
#endif

        if (isPro)
        {
            ProStatusText.Text            = "✦  Pro — all features unlocked";
            ProStatusText.Foreground      = (System.Windows.Media.Brush)FindResource("AccentLight");
            ProStatusBanner.Background    = (System.Windows.Media.Brush)FindResource("AccentMuted");
            ProStatusBanner.BorderBrush   = (System.Windows.Media.Brush)FindResource("AccentBorder");
            ProStatusBanner.BorderThickness = new Thickness(1);
#if DEBUG
            UnlockPanel.Visibility = Visibility.Collapsed;   // already activated
#endif
        }
        else
        {
            ProStatusText.Text            = "○  Free tier";
            ProStatusText.Foreground      = (System.Windows.Media.Brush)FindResource("MutedText");
            ProStatusBanner.Background    = (System.Windows.Media.Brush)FindResource("CardBackground");
            ProStatusBanner.BorderBrush   = (System.Windows.Media.Brush)FindResource("BorderBrush");
            ProStatusBanner.BorderThickness = new Thickness(1);
#if DEBUG
            UnlockPanel.Visibility = Visibility.Visible;
#endif
            // Release: UnlockPanel stays Collapsed (its XAML default) and never renders.
        }
    }

#if DEBUG
    private void ActivateCode_Click(object sender, RoutedEventArgs e)
    {
        var code = UnlockCodeBox.Text.Trim();

        // The expected hash is NOT stored in source (audit SC-01) — it lived here as a
        // literal and was readable by anyone browsing the public repo. It now comes from
        // a local dev environment variable, so nothing secret is committed:
        //     setx STREAMCMD_DEV_UNLOCK_SHA256 "<uppercase hex sha-256 of your code>"
        // This whole handler is DEBUG-only and absent from release builds regardless.
        var expectedHash = Environment.GetEnvironmentVariable("STREAMCMD_DEV_UNLOCK_SHA256");
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            UnlockResultText.Text       = "✗  Dev unlock not configured (set STREAMCMD_DEV_UNLOCK_SHA256).";
            UnlockResultText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            UnlockResultText.Visibility = Visibility.Visible;
            return;
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = BitConverter.ToString(
            sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(code)))
            .Replace("-", "").ToUpperInvariant();

        if (CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hash),
                Encoding.UTF8.GetBytes(expectedHash.Trim().ToUpperInvariant())))
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
#endif

    // ── External links ───────────────────────────────────────────────────────

    private void OpenGoogleCloud_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://console.cloud.google.com/apis/library/youtube.googleapis.com");

    private void OpenSE_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://streamelements.com/dashboard/account/channels");
}
