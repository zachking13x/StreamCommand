using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamCommand.Models;
using StreamCommand.Services;

namespace StreamCommand.Views;

public class ChatMessage
{
    public string Time        { get; set; } = "";
    public string Badge       { get; set; } = "";
    public string UserColored { get; set; } = "";
    public Brush  UserColor   { get; set; } = Brushes.White;
    public string Message     { get; set; } = "";
    public bool   IsAlert     { get; set; }
}

public partial class ChatMonitorView : UserControl
{
    private readonly ObservableCollection<ChatMessage> _messages = new();
    // Use the shared singleton so AutomationEngine receives the same events
    private readonly TwitchChatService _chat = TwitchChatService.Shared;
    private string _filter = "all";

    public ChatMonitorView()
    {
        InitializeComponent();
        ChatList.ItemsSource = _messages;

        _chat.MessageReceived += msg => Dispatcher.Invoke(() => AddMessage(msg));
        _chat.StatusChanged   += status => Dispatcher.Invoke(() =>
        {
            if (ConnectStatusText != null)
                ConnectStatusText.Text = status;
        });

        _chat.Connected += () => Dispatcher.Invoke(() =>
        {
            // Hide the "add your token" prompt once we're live on Twitch
            ConnectBanner.Visibility = Visibility.Collapsed;
        });

        // Auto-connect if settings are already filled in
        Loaded += async (_, _) =>
        {
            await TryAutoConnectAsync();
            BuildCommandsList();
        };
    }

    // ── Connection ───────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task TryAutoConnectAsync()
    {
        var s = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(s.TwitchUsername) || string.IsNullOrWhiteSpace(s.TwitchChatToken))
            return;   // no credentials yet — show mock messages

        // Clear mock messages and connect for real
        _messages.Clear();
        await _chat.ConnectAsync(s.TwitchUsername, s.TwitchUsername, s.TwitchChatToken);
    }

    private void AddMessage(TwitchChatMessage msg)
    {
        // Apply current filter
        if (_filter == "subs"        && !msg.IsSub   && !msg.IsAlert) return;
        if (_filter == "highlighted" && !msg.IsAlert) return;

        string badge = msg.IsMod ? "🔧" : msg.IsSub ? "⭐" : msg.IsVip ? "💎" : "";

        Brush color;
        try { color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(msg.Color)); }
        catch { color = new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC)); }

        _messages.Add(new ChatMessage
        {
            Time        = msg.Time.ToString("h:mm tt"),
            Badge       = badge,
            UserColored = msg.Username + ":",
            UserColor   = color,
            Message     = msg.Text,
            IsAlert     = msg.IsAlert
        });

        // Raise alert for subs / raids / bits
        if (msg.IsAlert)
            StreamEvents.RaiseAlert(badge.Length > 0 ? badge : "🔔", msg.Text);

        // Auto-scroll to bottom
        ChatScroller.ScrollToBottom();
    }

    // ── Command management ───────────────────────────────────────────────────

    private void BuildCommandsList()
    {
        CommandsPanel.Children.Clear();
        var s = SettingsService.Load();

        foreach (var cmd in s.ChatCommands)
        {
            var captured = cmd;
            var row = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderBrush     = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(8, 6, 8, 6),
                Margin          = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Toggle checkbox
            var toggle = new CheckBox
            {
                IsChecked       = cmd.IsEnabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin          = new Thickness(0, 0, 8, 0)
            };
            toggle.Checked   += (_, _) => SetCommandEnabled(captured.Trigger, true);
            toggle.Unchecked += (_, _) => SetCommandEnabled(captured.Trigger, false);

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text       = cmd.Trigger,
                Foreground = (Brush)FindResource("AccentLight"),
                FontSize   = 12, FontWeight = FontWeights.SemiBold
            });
            info.Children.Add(new TextBlock
            {
                Text        = cmd.Response,
                Foreground  = (Brush)FindResource("MutedText"),
                FontSize    = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin      = new Thickness(0, 2, 0, 0)
            });

            Grid.SetColumn(toggle, 0);
            Grid.SetColumn(info, 1);
            grid.Children.Add(toggle);
            grid.Children.Add(info);
            row.Child = grid;
            CommandsPanel.Children.Add(row);
        }
    }

    private void SetCommandEnabled(string trigger, bool enabled)
    {
        var s = SettingsService.Load();
        var cmd = s.ChatCommands.FirstOrDefault(c => c.Trigger == trigger);
        if (cmd is not null) cmd.IsEnabled = enabled;
        SettingsService.Save(s);
    }

    private void AddCommand_Click(object sender, RoutedEventArgs e)
    {
        AddCommandForm.Visibility = Visibility.Visible;
        NewTriggerBox.Text  = "!";
        NewResponseBox.Text = "";
        NewTriggerBox.Focus();
    }

    private void CancelCommand_Click(object sender, RoutedEventArgs e)
        => AddCommandForm.Visibility = Visibility.Collapsed;

    private void SaveCommand_Click(object sender, RoutedEventArgs e)
    {
        var trigger  = NewTriggerBox.Text.Trim();
        var response = NewResponseBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(response)) return;
        if (!trigger.StartsWith("!")) trigger = "!" + trigger;

        var s = SettingsService.Load();
        s.ChatCommands.Add(new ChatCommand { Trigger = trigger, Response = response, IsEnabled = true });
        SettingsService.Save(s);

        AddCommandForm.Visibility = Visibility.Collapsed;
        BuildCommandsList();
    }

    // ── Filters ──────────────────────────────────────────────────────────────

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn) _filter = btn.Tag?.ToString() ?? "all";
    }

    // ── External links ───────────────────────────────────────────────────────

    private void GoToSettings_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("settings");

    private void OpenTwitch_Click(object sender, RoutedEventArgs e)
        => AppLaunchService.OpenUrl("https://dashboard.twitch.tv");
}
