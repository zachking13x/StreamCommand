using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private readonly TwitchChatService _chat = new();
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
        Loaded += async (_, _) => await TryAutoConnectAsync();
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

        // Auto-scroll to bottom
        ChatScroller.ScrollToBottom();
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
