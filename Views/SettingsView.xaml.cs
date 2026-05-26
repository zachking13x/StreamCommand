using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        if (int.TryParse(OBSPort.Text, out var port)) _settings.OBSWebSocketPort = port;

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
        ConnectTwitchBtn.IsEnabled = false;
        ConnectTwitchBtn.Content   = "Opening Twitch…";

        try
        {
            var result = await TwitchOAuthService.AuthorizeAsync();

            if (result != null)
            {
                _settings.TwitchUsername     = result.Username;
                _settings.TwitchChatToken    = result.AccessToken;
                _settings.TwitchRefreshToken = result.RefreshToken;   // PKCE refresh token
                _settings.TwitchClientId     = result.ClientId;
                SettingsService.Save(_settings);

                TwitchUsername.Text            = result.Username;
                TwitchConnectedText.Text       = $"Connected as @{result.Username}";
                TwitchConnectedBanner.Visibility = Visibility.Visible;
                ConnectTwitchBtn.Content        = BuildConnectBtnContent("Reconnect Twitch");

                // Flash the saved banner so the user knows settings were written
                SavedBanner.Visibility = Visibility.Visible;
                await Task.Delay(2500);
                SavedBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                ConnectTwitchBtn.Content = BuildConnectBtnContent("Connection failed — try again");
            }
        }
        catch
        {
            ConnectTwitchBtn.Content = BuildConnectBtnContent("Connection failed — try again");
        }
        finally
        {
            ConnectTwitchBtn.IsEnabled = true;
        }
    }

    // ── External links ───────────────────────────────────────────────────────

    private void OpenGoogleCloud_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://console.cloud.google.com/apis/library/youtube.googleapis.com");

    private void OpenSE_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://streamelements.com/dashboard/account/channels");
}
