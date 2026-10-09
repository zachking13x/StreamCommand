using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using StreamCommand.Services;
using TC = StreamCommand.Services.ThemeColors;

namespace StreamCommand.Views;

public partial class DashboardView : UserControl
{
    private DispatcherTimer? _sessionTimer;
    private DateTime         _sessionStart;
    private bool             _conversionCardShown;

    // Recent events ring buffer (newest-first, max 5)
    private readonly List<(string Emoji, string Label, string User, DateTime Time)> _recentEvents = new();

    public DashboardView()
    {
        InitializeComponent();

        TodayDate.Text = DateTime.Now.ToString("dddd, MMMM d");

        Loaded += async (_, _) =>
        {
            await RefreshFollowerCountAsync();
            EvaluateConversionCard();
            LoadScenes();
            InitChatEmptyState();
        };

        OBSWebSocketService.Shared.StateChanged += st => Dispatcher.Invoke(() =>
        {
            UpdateOBSPill(st);
            // Drop the scene card back to its empty state the moment OBS goes away.
            if (st != OBSState.Connected)
                ShowSceneEmptyState();
        });
        StreamEvents.ChecklistProgressChanged += (d, t) => Dispatcher.Invoke(() => UpdateChecklistCard(d, t));
        StreamEvents.UsageUpdated         += () => Dispatcher.Invoke(EvaluateConversionCard);
        StreamEvents.StreamStateChanged   += l  => Dispatcher.Invoke(() => OnStreamStateChanged(l));
        StreamEvents.AutomationFired      += (trigger, user) => Dispatcher.Invoke(() => AddRecentEvent(trigger, user));

        OBSWebSocketService.Shared.ScenesLoaded += scenes => Dispatcher.Invoke(() => BuildSceneButtons(scenes));

        TwitchChatService.Shared.MessageReceived += msg =>
            Dispatcher.Invoke(() => AddChatMessage(msg));

        EntitlementService.Refreshed += () => Dispatcher.Invoke(EvaluateConversionCard);

        // Initialise OBS pill from current state
        UpdateOBSPill(OBSWebSocketService.Shared.State);
    }

    // ── Conversion card ───────────────────────────────────────────────────────

    private void EvaluateConversionCard()
    {
        if (_conversionCardShown) return;
        if (EntitlementService.IsPro) return;

        var s = SettingsService.Load();
        if (s.StreamValueCardDismissed || s.StreamsCompleted < 3) return;

        _conversionCardShown = true;
        var card = new StreamValueSummaryCard();
        card.Dismissed += () =>
        {
            ConversionCardHost.Children.Clear();
            _conversionCardShown = false;
        };
        ConversionCardHost.Children.Clear();
        ConversionCardHost.Children.Add(card);
    }

    // ── Follower count ─────────────────────────────────────────────────────

    private async Task RefreshFollowerCountAsync()
    {
        var s = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(s.TwitchUsername) ||
            string.IsNullOrWhiteSpace(s.TwitchClientId)  ||
            string.IsNullOrWhiteSpace(s.TwitchChatToken))
            return;

        var stats = await TwitchApiService.GetChannelStatsAsync(
            s.TwitchUsername, s.TwitchClientId, s.TwitchChatToken);
        if (stats == null) return;

        if (stats.FollowerCount >= 0)
            FollowerCountText.Text = stats.FollowerCount.ToString("N0");

        if (stats.IsLive)
            LiveViewersText.Text = stats.ViewerCount.ToString("N0");

        CheckMilestones(stats.FollowerCount);
    }

    private static void CheckMilestones(int followerCount)
    {
        if (followerCount <= 0) return;
        int[] milestones = { 100, 500, 1000, 5000, 10000 };
        var s = SettingsService.Load();
        bool changed = false;

        foreach (var threshold in milestones)
        {
            if (followerCount < threshold) continue;
            var label = threshold.ToString();
            if (s.CelebratedMilestones.Contains(label)) continue;
            s.CelebratedMilestones.Add(label);
            changed = true;
        }

        if (changed) SettingsService.Save(s);
    }

    // ── Stream state / session timer ──────────────────────────────────────────

    private void OnStreamStateChanged(bool isLive)
    {
        if (isLive)
        {
            _sessionStart = DateTime.UtcNow;
            ChatLiveDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));

            _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionTimer.Tick += (_, _) =>
            {
                var elapsed = DateTime.UtcNow - _sessionStart;
                SessionTimerText.Text = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                    : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            };
            _sessionTimer.Start();
        }
        else
        {
            _sessionTimer?.Stop();
            _sessionTimer     = null;
            SessionTimerText.Text = "—";
            LiveViewersText.Text  = "—";
            ChatLiveDot.Fill = new SolidColorBrush(TC.MutedText);
        }
    }

    // ── OBS pill ──────────────────────────────────────────────────────────────

    private void UpdateOBSPill(OBSState state)
    {
        var (c, label) = state switch
        {
            OBSState.Connected    => (Color.FromRgb(0x22, 0xC5, 0x5E), "OBS: Connected"),
            OBSState.Connecting   => (Color.FromRgb(0xF5, 0x9E, 0x0B), "OBS: Connecting…"),
            OBSState.Reconnecting => (Color.FromRgb(0xF5, 0x9E, 0x0B), "OBS: Reconnecting…"),
            _                     => (Color.FromRgb(0xEF, 0x44, 0x44), "OBS: Offline")
        };
        var brush = new SolidColorBrush(c);

        OBSPill.BorderBrush    = brush;
        OBSPillDot.Fill        = brush;
        OBSPillText.Foreground = brush;
        OBSPillText.Text       = label;
        OBSPill.Background     = new SolidColorBrush(Color.FromArgb(0x20, c.R, c.G, c.B));
    }

    // ── Checklist card ────────────────────────────────────────────────────────

    private void UpdateChecklistCard(int done, int total)
    {
        ChecklistBadgeText.Text = total > 0 ? $"{done} / {total}" : "—";

        double pct = total > 0 ? done * 100.0 / total : 0;
        ChecklistPct.Text = $"{(int)pct}%";

        // Draw ring via StrokeDashArray on a 68x68 ellipse circumference ≈ 213.6
        const double circumference = 213.6;
        double filled = circumference * (pct / 100.0);
        ChecklistRing.StrokeDashArray = new DoubleCollection { filled, circumference - filled };

        if (done == total && total > 0)
        {
            ChecklistSubText.Text              = "✓  All tasks complete — you're ready!";
            ChecklistRing.Stroke               = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            ChecklistBadge.Background          = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xC5, 0x5E));
            ChecklistBadge.BorderBrush         = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            ChecklistBadgeText.Foreground      = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
        }
        else
        {
            ChecklistSubText.Text = done == 0
                ? "Open checklist to start your pre-stream setup"
                : $"{total - done} task{(total - done == 1 ? "" : "s")} remaining";
        }
    }

    private void OpenChecklist_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("pre-stream");

    // ── Scene control ─────────────────────────────────────────────────────────

    private void LoadScenes()
    {
        if (OBSWebSocketService.Shared.State == OBSState.Connected)
            _ = Task.Run(async () =>
            {
                var scenes = await OBSWebSocketService.Shared.GetScenesAsync();
                Dispatcher.Invoke(() => BuildSceneButtons(scenes));
            });
    }

    private void BuildSceneButtons(string[] scenes)
    {
        SceneButtonsPanel.Children.Clear();

        if (scenes.Length == 0)
        {
            ShowSceneEmptyState();
            return;
        }

        // Connected with scenes — show the real controls, hide the empty state.
        SceneEmptyState.Visibility        = Visibility.Collapsed;
        SceneConnectedContent.Visibility  = Visibility.Visible;
        ActiveSceneText.Text              = scenes.LastOrDefault() ?? "—";

        foreach (var scene in scenes.Take(4))
        {
            var captured = scene;
            var btn = new Button
            {
                Content   = captured,
                Style     = (Style)FindResource("SecondaryButton"),
                Margin    = new Thickness(0, 0, 0, 6),
                Padding   = new Thickness(10, 7, 10, 7),
                FontSize  = 12,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            btn.Click += async (_, _) =>
            {
                await OBSWebSocketService.Shared.SetSceneAsync(captured);
                ActiveSceneText.Text = captured;
            };
            SceneButtonsPanel.Children.Add(btn);
        }
    }

    private void ShowSceneEmptyState()
    {
        SceneConnectedContent.Visibility = Visibility.Collapsed;
        SceneEmptyState.Visibility       = Visibility.Visible;
    }

    private void OpenSetup_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("setup-guide");

    // ── Recent events ─────────────────────────────────────────────────────────

    private void AddRecentEvent(string triggerLabel, string user)
    {
        var (emoji, color) = triggerLabel switch
        {
            "NewFollower"    => ("♥", Color.FromRgb(0x00, 0xC9, 0xA7)),  // teal
            "NewSubscriber"  => ("⭐", Color.FromRgb(0x7C, 0x3A, 0xED)), // purple
            "BitsReceived"   => ("💎", Color.FromRgb(0xF5, 0x9E, 0x0B)), // amber
            _                => ("⚡", Color.FromRgb(0x6B, 0x80, 0x99))   // muted
        };

        _recentEvents.Insert(0, (emoji, triggerLabel, user, DateTime.Now));
        if (_recentEvents.Count > 5) _recentEvents.RemoveAt(5);

        RebuildRecentEventsPanel();
    }

    private void RebuildRecentEventsPanel()
    {
        RecentEventsEmpty.Visibility = _recentEvents.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        RecentEventsPanel.Children.Clear();
        foreach (var (emoji, label, user, time) in _recentEvents)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var color = label switch
            {
                "NewFollower"   => Color.FromRgb(0x00, 0xC9, 0xA7),
                "NewSubscriber" => Color.FromRgb(0x7C, 0x3A, 0xED),
                "BitsReceived"  => Color.FromRgb(0xF5, 0x9E, 0x0B),
                _               => Color.FromRgb(0x6B, 0x80, 0x99)
            };

            var dot = new Ellipse
            {
                Width  = 8, Height = 8,
                Fill   = new SolidColorBrush(color),
                Margin = new Thickness(0, 3, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text       = $"{emoji} {label.Replace("New", "").Replace("Received", "")} — {user}",
                Foreground = new SolidColorBrush(TC.PrimaryText),
                FontSize   = 12
            });

            var timestamp = new TextBlock
            {
                Text       = time.ToString("h:mm tt"),
                Foreground = new SolidColorBrush(TC.MutedText),
                FontSize   = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(info, 1);
            Grid.SetColumn(timestamp, 2);
            row.Children.Add(dot);
            row.Children.Add(info);
            row.Children.Add(timestamp);

            // Slide-in animation
            row.RenderTransform = new TranslateTransform(0, -8);
            row.Opacity         = 0;
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            var slideAnim = new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(300));
            row.BeginAnimation(UIElement.OpacityProperty, anim);
            ((TranslateTransform)row.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideAnim);

            RecentEventsPanel.Children.Add(row);
        }
    }

    // ── Live chat panel ───────────────────────────────────────────────────────

    private int _chatCount;

    /// <summary>Hide the chat empty state once Twitch credentials exist (connection pending/live).</summary>
    private void InitChatEmptyState()
    {
        var s = SettingsService.Load();
        bool hasTwitch = !string.IsNullOrWhiteSpace(s.TwitchUsername)
                      && !string.IsNullOrWhiteSpace(s.TwitchChatToken);
        ChatEmptyState.Visibility = hasTwitch ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddChatMessage(TwitchChatMessage msg)
    {
        if (msg.IsAlert) return;  // alerts go to overlay, not chat panel

        ChatEmptyState.Visibility = Visibility.Collapsed;
        _chatCount++;
        ChatCountText.Text = $"({_chatCount})";

        // Random-ish color per user (hash-based so same user = same color each message)
        var hue = Math.Abs(msg.Username.GetHashCode()) % 360;
        var userColor = HslToColor(hue, 0.7, 0.65);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 3, 10, 3) };
        row.Children.Add(new TextBlock
        {
            Text       = msg.Username + ": ",
            Foreground = new SolidColorBrush(userColor),
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold
        });
        row.Children.Add(new TextBlock
        {
            Text         = msg.Text,
            Foreground   = new SolidColorBrush(TC.PrimaryText),
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 170
        });

        ChatMessagePanel.Children.Add(row);

        // Cap at 80 messages
        while (ChatMessagePanel.Children.Count > 80)
            ChatMessagePanel.Children.RemoveAt(0);

        // Auto-scroll to bottom
        ChatScrollViewer.ScrollToEnd();
    }

    private void ChatSend_Click(object sender, RoutedEventArgs e)
        => SendChatMessage();

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SendChatMessage();
    }

    private async void SendChatMessage()
    {
        var text = ChatInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        ChatInputBox.Text = string.Empty;

        var s = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(s.TwitchUsername)) return;

        await TwitchChatService.Shared.SendMessageAsync(s.TwitchUsername, text);
    }

    private void GoLive_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("live-control");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Color HslToColor(double h, double s, double l)
    {
        double c  = (1 - Math.Abs(2 * l - 1)) * s;
        double x  = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m  = l - c / 2;
        double r, g, b;

        if      (h < 60)  { r = c;  g = x;  b = 0; }
        else if (h < 120) { r = x;  g = c;  b = 0; }
        else if (h < 180) { r = 0;  g = c;  b = x; }
        else if (h < 240) { r = 0;  g = x;  b = c; }
        else if (h < 300) { r = x;  g = 0;  b = c; }
        else              { r = c;  g = 0;  b = x; }

        return Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
