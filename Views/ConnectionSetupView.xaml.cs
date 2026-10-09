using StreamCommand.Services;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace StreamCommand.Views;

public partial class ConnectionSetupView : UserControl
{
    private CancellationTokenSource? _authCts;

    public ConnectionSetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStatus();
        StreamEvents.OBSStateChanged += _ => Dispatcher.Invoke(RefreshStatus);
    }

    private void RefreshStatus()
    {
        var s = SettingsService.Load();
        bool twitchConnected = !string.IsNullOrWhiteSpace(s.TwitchChatToken);
        bool obsConnected    = OBSWebSocketService.Shared.State == OBSState.Connected;

        TwitchConnectedBadge.Visibility = twitchConnected ? Visibility.Visible : Visibility.Collapsed;
        ReconnectTwitchBtn.Visibility   = twitchConnected ? Visibility.Visible : Visibility.Collapsed;
        ConnectTwitchBtn.Visibility     = twitchConnected ? Visibility.Collapsed : Visibility.Visible;
        TwitchConnectedUsername.Text    = s.TwitchUsername;
        TwitchStatusText.Text           = twitchConnected
            ? $"Connected as {s.TwitchUsername}"
            : "Not connected — StreamCommand needs your Twitch account to read chat and send automation messages.";

        OBSConnectedBadge.Visibility = obsConnected ? Visibility.Visible : Visibility.Collapsed;
        OBSStatusText.Text           = obsConnected
            ? "OBS is connected. Scene control and stream start/stop are active."
            : "Not connected — OBS must be running with WebSocket Server enabled.";

        OBSPortBox.Text = s.OBSWebSocketPort.ToString();
    }

    private async void ConnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        _authCts?.Cancel();
        _authCts = new CancellationTokenSource();

        ConnectTwitchBtn.IsEnabled    = false;
        ReconnectTwitchBtn.IsEnabled  = false;
        ConnectTwitchBtn.Content      = "Connecting…";
        DeviceFlowPanel.Visibility    = Visibility.Collapsed;
        DeviceCodeText.Text           = "------";

        try
        {
            var result = await TwitchOAuthService.AuthorizeAsync(
                onCodeReady: (userCode, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        DeviceFlowPanel.Visibility = Visibility.Visible;
                        DeviceCodeText.Text        = userCode;
                        TwitchStatusText.Text      = "Waiting for you to activate at twitch.tv/activate…";
                    });
                },
                cancellationToken: _authCts.Token);

            if (result == null)
            {
                var failure = TwitchOAuthService.LastFailure;
                TwitchStatusText.Text      = failure != null
                    ? $"Authorization failed: {failure.Detail}"
                    : "Authorization timed out. Click Connect to try again.";
                DeviceFlowPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var s = SettingsService.Load();
            s.TwitchChatToken     = result.AccessToken;
            s.TwitchRefreshToken  = result.RefreshToken;
            s.TwitchUsername      = result.Username;
            s.TwitchEverConnected = true;
            SettingsService.Save(s);
            _ = TwitchChatService.Shared.StartFromSettingsAsync();

            DeviceFlowPanel.Visibility = Visibility.Collapsed;
            RefreshStatus();
        }
        catch (OperationCanceledException) { /* user cancelled */ }
        catch
        {
            TwitchStatusText.Text = "An error occurred. Please try again.";
        }
        finally
        {
            ConnectTwitchBtn.IsEnabled   = true;
            ReconnectTwitchBtn.IsEnabled = true;
            ConnectTwitchBtn.Content     = "Connect Twitch";
        }
    }

    private async void ConnectOBS_Click(object sender, RoutedEventArgs e)
    {
        OBSErrorPanel.Visibility = Visibility.Collapsed;

        if (!int.TryParse(OBSPortBox.Text.Trim(), out int port) || port < 1 || port > 65535)
        {
            OBSErrorText.Text        = "Invalid port number. Use 4455 (default) unless you changed it in OBS.";
            OBSErrorPanel.Visibility = Visibility.Visible;
            return;
        }

        var s = SettingsService.Load();
        s.OBSWebSocketPort = port;
        SettingsService.Save(s);

        bool ok = await OBSWebSocketService.Shared.ConnectAsync("localhost", port, OBSPasswordBox.Text.Trim());
        if (!ok)
        {
            OBSErrorText.Text        = OBSWebSocketService.Shared.StatusMessage;
            OBSErrorPanel.Visibility = Visibility.Visible;
        }

        RefreshStatus();
    }

    private void TwitchHelp_Toggle(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => TwitchHelpPanel.Visibility = TwitchHelpPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void OBSHelp_Toggle(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OBSHelpPanel.Visibility = OBSHelpPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
}
