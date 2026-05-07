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
        TwitchClientId.Text   = _settings.TwitchClientId;
        OBSPort.Text          = _settings.OBSWebSocketPort.ToString();

        // PasswordBoxes can't display existing values — show a saved-indicator instead
        // so users know the field is already populated and they only need to type if changing it.
        TwitchClientSecretSaved.Visibility  = !string.IsNullOrEmpty(_settings.TwitchClientSecret)  ? Visibility.Visible : Visibility.Collapsed;
        YoutubeApiKeySaved.Visibility        = !string.IsNullOrEmpty(_settings.YoutubeApiKey)        ? Visibility.Visible : Visibility.Collapsed;
        SeTokenSaved.Visibility              = !string.IsNullOrEmpty(_settings.StreamElementsToken)  ? Visibility.Visible : Visibility.Collapsed;
        OBSPasswordSaved.Visibility          = !string.IsNullOrEmpty(_settings.OBSWebSocketPassword) ? Visibility.Visible : Visibility.Collapsed;
        TwitchChatTokenSaved.Visibility      = !string.IsNullOrEmpty(_settings.TwitchChatToken)      ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.TwitchUsername   = TwitchUsername.Text.Trim();
        _settings.YoutubeChannelId = YoutubeChannelId.Text.Trim();
        _settings.DiscordInvite    = DiscordInvite.Text.Trim();
        _settings.TwitchClientId   = TwitchClientId.Text.Trim();

        // Only overwrite a secret if the user actually typed something — otherwise
        // keep the previously saved value.  PasswordBoxes are blank by design and
        // "blank" does NOT mean "the user wants to clear the credential".
        if (TwitchClientSecret.Password.Length > 0)  _settings.TwitchClientSecret   = TwitchClientSecret.Password;
        if (YoutubeApiKey.Password.Length > 0)        _settings.YoutubeApiKey         = YoutubeApiKey.Password;
        if (SeToken.Password.Length > 0)              _settings.StreamElementsToken   = SeToken.Password;
        if (OBSPassword.Password.Length > 0)          _settings.OBSWebSocketPassword  = OBSPassword.Password;
        if (TwitchChatToken.Password.Length > 0)      _settings.TwitchChatToken       = TwitchChatToken.Password;

        if (int.TryParse(OBSPort.Text, out var port)) _settings.OBSWebSocketPort = port;

        SettingsService.Save(_settings);
        LoadIntoFields();   // refresh saved-indicators after save

        SavedBanner.Visibility = Visibility.Visible;
        SaveBtn.Content = "✓  Saved!";
        await Task.Delay(2500);
        SavedBanner.Visibility = Visibility.Collapsed;
        SaveBtn.Content = "💾  Save Settings";
    }

    private void OpenTwitchDev_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://dev.twitch.tv/console/apps");

    private void OpenGoogleCloud_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://console.cloud.google.com/apis/library/youtube.googleapis.com");

    private void OpenSE_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://streamelements.com/dashboard/account/channels");

    private void OpenTMI_Click(object sender, MouseButtonEventArgs e)
        => AppLaunchService.OpenUrl("https://twitchapps.com/tmi/");
}
