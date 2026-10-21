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

    // Recent events ring buffer (newest-first)
    private const int MaxRecentEvents = 8;
    private readonly List<(string Label, string User, DateTime Time)> _recentEvents = new();

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
        OBSWebSocketService.Shared.ProgramSceneChanged += scene => Dispatcher.BeginInvoke(() =>
        {
            ActiveSceneText.Text = scene;
            HighlightActiveScene();
        });

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
        SessionLabel.Text = isLive ? "ON AIR" : "SESSION";
        SessionLabel.SetResourceReference(TextBlock.ForegroundProperty, isLive ? "OnAirText" : "LabelText");
        OnAirDot.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;

        if (isLive)
        {
            _sessionStart = DateTime.UtcNow;
            ChatLiveDot.SetResourceReference(Shape.FillProperty, "LiveBrush");

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
            ChatLiveDot.SetResourceReference(Shape.FillProperty, "MutedText");
        }
    }

    // ── OBS pill ──────────────────────────────────────────────────────────────

    private void UpdateOBSPill(OBSState state)
    {
        // (dot, text, fill, border) theme keys — teal reads "ready", amber "working on it",
        // and offline is deliberately quiet rather than alarm-red: it's the normal pre-stream state.
        var (dot, label, fg, bg, border) = state switch
        {
            OBSState.Connected    => ("AccentBrush",  "Connected",       "AccentLight",   "AccentMuted",    "AccentBorder"),
            OBSState.Connecting   => ("WarningBrush", "Connecting…",     "WarningBrush",  "InsetBackground", "BorderLight"),
            OBSState.Reconnecting => ("WarningBrush", "Reconnecting…",   "WarningBrush",  "InsetBackground", "BorderLight"),
            _                     => ("MutedText",    "Not connected",   "SecondaryText", "InsetBackground", "BorderLight")
        };

        OBSPillText.Text = label;
        OBSPillDot.SetResourceReference(Shape.FillProperty, dot);
        OBSPillText.SetResourceReference(TextBlock.ForegroundProperty, fg);
        OBSPill.SetResourceReference(Border.BackgroundProperty, bg);
        OBSPill.SetResourceReference(Border.BorderBrushProperty, border);
    }

    // ── Checklist card ────────────────────────────────────────────────────────

    private void UpdateChecklistCard(int done, int total)
    {
        ChecklistBadgeText.Text = total > 0 ? $"{done} / {total}" : "—";

        double fraction = total > 0 ? (double)done / total : 0;
        ChecklistPct.Text = $"{(int)(fraction * 100)}%";

        double diameter = ChecklistRing.ActualWidth > 0 ? ChecklistRing.ActualWidth : 112;
        var (filled, gap) = ProgressRingMath.Dashes(fraction, diameter, ChecklistRing.StrokeThickness);
        ChecklistRing.StrokeDashArray = new DoubleCollection { filled, gap };
        // A zero-length dash with round caps still paints a dot — hide the arc at 0%.
        ChecklistRing.Visibility = done > 0 ? Visibility.Visible : Visibility.Hidden;

        bool complete = done == total && total > 0;
        ChecklistRing.SetResourceReference(Shape.StrokeProperty, complete ? "SuccessBrush" : "RingGradient");
        ChecklistSubText.Text = complete ? "All tasks complete — you're ready to go live."
                              : done == 0 ? "Open the checklist to start your pre-stream setup"
                              : $"{total - done} task{(total - done == 1 ? "" : "s")} left before you go live";
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

        foreach (var (scene, index) in scenes.Take(4).Select((s, i) => (s, i)))
        {
            var captured = scene;
            var btn = new Button
            {
                Content   = SceneTileContent(captured, HotkeyBindings.SceneKey(index)),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag       = captured,
                Style     = (Style)FindResource("SceneTile"),
                Margin    = new Thickness(0, 0, 8, 8),
                Padding   = new Thickness(14, 0, 14, 0),
                Height    = 46,
            };
            btn.Click += async (_, _) =>
            {
                await OBSWebSocketService.Shared.SetSceneAsync(captured);
                ActiveSceneText.Text = captured;
                HighlightActiveScene();
            };
            SceneButtonsPanel.Children.Add(btn);
        }
        HighlightActiveScene();
    }

    /// <summary>Scene name on the left, its global F-key on the right.</summary>
    private UIElement SceneTileContent(string name, string key)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (key.Length > 0)
        {
            var chip = new Border { Style = (Style)FindResource("KeyChip"), Margin = new Thickness(8, 0, 0, 0),
                                    Child = new TextBlock { Text = key, Style = (Style)FindResource("KeyChipText") } };
            Grid.SetColumn(chip, 1);
            grid.Children.Add(chip);
        }
        return grid;
    }

    // The tile matching what's on program gets the accent edge + glow; the rest fall back to the style.
    private void HighlightActiveScene()
    {
        foreach (var b in SceneButtonsPanel.Children.OfType<Button>())
        {
            bool active = (string?)b.Tag == ActiveSceneText.Text;
            if (active)
            {
                b.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
                b.SetResourceReference(Control.BackgroundProperty, "AccentMuted");
                b.SetResourceReference(Control.ForegroundProperty, "AccentLight");
                var glow = new System.Windows.Media.Effects.DropShadowEffect
                    { ShadowDepth = 0, BlurRadius = 16, Opacity = 0.45, Color = (Color)FindResource("AccentColor") };
                b.Effect = glow;
            }
            else
            {
                b.ClearValue(Control.BorderBrushProperty);
                b.ClearValue(Control.BackgroundProperty);
                b.ClearValue(Control.ForegroundProperty);
                b.Effect = null;
            }
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
        _recentEvents.Insert(0, (triggerLabel, user, DateTime.Now));
        if (_recentEvents.Count > MaxRecentEvents) _recentEvents.RemoveAt(MaxRecentEvents);

        RebuildRecentEventsPanel();
    }

    private void RebuildRecentEventsPanel()
    {
        RecentEventsEmpty.Visibility = _recentEvents.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        RecentEventsPanel.Children.Clear();
        foreach (var (label, user, time) in _recentEvents)
        {
            // Teal = follow, purple = sub, amber = bits — same keys as the legend in the card header.
            var (colorKey, color, action) = label switch
            {
                "NewFollower"   => ("FollowBrush", TC.Follow, "followed the channel"),
                "NewSubscriber" => ("SubBrush",    TC.Sub,    "subscribed"),
                "BitsReceived"  => ("BitsBrush",   TC.Bits,   "cheered bits"),
                _               => ("MutedText",   TC.MutedText, label)
            };

            var row = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Height          = 46,
            };
            row.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Child = grid;

            // Dot with a soft halo so the colour reads at a glance. WPF draws the stroke inside
            // the bounds, so 18px outer = solid core + translucent 4px ring.
            var dot = new Ellipse
            {
                Width = 18, Height = 18, Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                StrokeThickness = 4,
                Stroke = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B)),
            };
            dot.SetResourceReference(Shape.FillProperty, colorKey);

            var name = new TextBlock
            {
                Text = user, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");

            var what = new TextBlock { Text = action, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            what.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

            var timestamp = new TextBlock
            {
                Text = time.ToString("h:mm tt"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            timestamp.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(what, 2);
            Grid.SetColumn(timestamp, 3);
            grid.Children.Add(dot);
            grid.Children.Add(name);
            grid.Children.Add(what);
            grid.Children.Add(timestamp);

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
        ChatCountText.Text = $"{_chatCount} message{(_chatCount == 1 ? "" : "s")}";

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

#if DEBUG
    // ── Store screenshot demo (Debug builds only — see Dev/StoreCapture.cs) ──────
    // Fills the dashboard with a believable mid-stream state. Never compiled into Release.
    public void ApplyStoreDemo()
    {
        UpdateOBSPill(OBSState.Connected);
        OnStreamStateChanged(true);
        _sessionStart          = DateTime.UtcNow - new TimeSpan(1, 42, 18);
        SessionTimerText.Text  = "1:42:18";
        LiveViewersText.Text   = "248";
        FollowerCountText.Text = "12,480";
        NewFollowersTodayText.Text = "+37";

        BuildSceneButtons(["Starting Soon", "Gameplay", "Just Chatting", "Be Right Back"]);
        ActiveSceneText.Text = "Gameplay";
        HighlightActiveScene();

        UpdateChecklistCard(5, 6);

        _recentEvents.Clear();
        var now = DateTime.Now;
        foreach (var (label, user, minsAgo) in new[]
        {
            ("BitsReceived", "emberkeys", 11), ("NewFollower", "saltglass", 9), ("NewSubscriber", "driftwood_gg", 7),
            ("NewFollower", "lunarlatch", 4), ("BitsReceived", "quietfrag", 2), ("NewFollower", "pixel_harbor", 1),
            ("NewSubscriber", "mossbyte", 0),
        })
            _recentEvents.Insert(0, (label, user, now.AddMinutes(-minsAgo)));
        RebuildRecentEventsPanel();

        ChatMessagePanel.Children.Clear();
        _chatCount = 0;
        foreach (var (u, t) in new[]
        {
            ("lunarlatch", "first time catching you live!"), ("quietfrag", "CLUTCH. no way that worked"),
            ("emberkeys", "what sens are you on?"), ("pixel_harbor", "gg that round"),
            ("saltglass", "the scene swap was instant lol"), ("mossbyte", "6 months already, let's go"),
            ("tarnished_ttv", "how are you switching scenes so fast"), ("driftwood_gg", "it's the hotkeys, chat"),
            ("novaplays", "Ctrl+Shift+F-keys, never leave the game"), ("emberkeys", "need that setup"),
            ("lunarlatch", "BRB scene was so smooth"), ("quietfrag", "one more round!!"),
            ("saltglass", "hydrate check"), ("pixel_harbor", "GG chat, that was insane"),
        })
            AddChatMessage(new TwitchChatMessage { Username = u, Text = t });
    }
#endif
}
