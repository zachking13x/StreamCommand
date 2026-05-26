using System;
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

    // ── Twitch OAuth ─────────────────────────────────────────────────────────

    private async void ConnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        ConnectTwitchBtn.IsEnabled    = false;
        TwitchErrorBanner.Visibility  = Visibility.Collapsed;
        ConnectTwitchBtn.Content      = "Waiting for Twitch…";

        try
        {
            var result = await TwitchOAuthService.AuthorizeAsync();

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

                // Show exactly why it failed so the user (or we) can diagnose it
                var failure = TwitchOAuthService.LastFailure;
                if (failure != null)
                {
                    TwitchErrorStep.Text         = $"⚠  Failed at: {failure.Step}";
                    TwitchErrorDetail.Text       = failure.Detail;
                    TwitchErrorBanner.Visibility = Visibility.Visible;
                }
            }
        }
        catch (Exception ex)
        {
            ConnectTwitchBtn.Content = BuildConnectBtnContent("Try again");
            TwitchErrorStep.Text         = "⚠  Unexpected error";
            TwitchErrorDetail.Text       = ex.Message;
            TwitchErrorBanner.Visibility = Visibility.Visible;
        }
        finally
        {
            ConnectTwitchBtn.IsEnabled = true;
        }
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

    // ── External links ───────────────────────────────────────────────────────

    private void OpenGoogleCloud_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://console.cloud.google.com/apis/library/youtube.googleapis.com");

    private void OpenSE_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://streamelements.com/dashboard/account/channels");
}
