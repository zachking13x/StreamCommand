using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using StreamCommand.Services;
using TC = StreamCommand.Services.ThemeColors;

namespace StreamCommand.Views;

public partial class DashboardView : UserControl
{
    // Fallback placeholder data — used when not connected to Twitch
    private static readonly int[] _placeholderData = { 280, 295, 310, 302, 318, 308, 325, 305, 315, 300, 320 };

    // Live viewer history — populated each minute while stream is live (max 20 points)
    private readonly List<int> _liveViewerHistory = new();
    private DispatcherTimer? _viewerPollTimer;

    private readonly List<(string Label, string Url, string Emoji)> _quickLinks = new()
    {
        ("Twitch Dashboard",  "https://dashboard.twitch.tv",              "🟣"),
        ("YouTube Studio",    "https://studio.youtube.com",               "🔴"),
        ("Discord Web",       "https://discord.com/app",                  "💬"),
        ("Pretzel Music",     "https://www.pretzel.rocks",                "🎵"),
        ("StreamElements",    "https://streamelements.com/dashboard",     "🟠")
    };

    // Milestone thresholds — celebrated once per threshold, stored in AppSettings
    private static readonly int[] _milestones = { 100, 500, 1000, 5000, 10000, 15000 };

    // Dashboard preview feed
    private Action<BitmapSource>? _dashPreviewHandler;

    public DashboardView()
    {
        InitializeComponent();

        // Load after layout so FindResource works
        Loaded += async (_, _) =>
        {
            BuildUpcomingList();
            await RefreshFollowerCountAsync();
        };

        BuildQuickLaunch();

        // OBS state changes
        StreamEvents.OBSStateChanged += isConnected =>
            Dispatcher.Invoke(() => UpdateOBSPill(isConnected));

        // Checklist progress
        StreamEvents.ChecklistProgressChanged += (done, total) =>
            Dispatcher.Invoke(() => UpdateChecklistCard(done, total));

        // Planner data changes — refresh the upcoming list live
        StreamEvents.PlannerChanged += () =>
            Dispatcher.Invoke(BuildUpcomingList);

        // Start/stop live viewer polling when stream goes live or offline
        StreamEvents.StreamStateChanged += isLive =>
            Dispatcher.Invoke(() => OnStreamStateChanged(isLive));

        // Re-evaluate preview Pro gate when entitlement changes
        EntitlementService.Refreshed += () => Dispatcher.Invoke(ApplyDashPreviewProGate);
    }

    // ── Follower count + milestone check ─────────────────────────────────────

    private async System.Threading.Tasks.Task RefreshFollowerCountAsync()
    {
        var s = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(s.TwitchUsername) ||
            string.IsNullOrWhiteSpace(s.TwitchClientId)  ||
            string.IsNullOrWhiteSpace(s.TwitchChatToken))
            return;

        var stats = await TwitchApiService.GetChannelStatsAsync(
            s.TwitchUsername, s.TwitchClientId, s.TwitchChatToken);
        if (stats == null) return;

        Dispatcher.Invoke(() =>
        {
            if (stats.FollowerCount >= 0)
            {
                FollowerCountText.Text = stats.FollowerCount.ToString("N0");
                FollowerSubText.Text   = "Total followers";
            }

            if (stats.IsLive)
                LiveViewersText.Text = stats.ViewerCount.ToString("N0");

            CheckMilestones(stats.FollowerCount);
        });
    }

    private static void CheckMilestones(int followerCount)
    {
        if (followerCount <= 0) return;
        var s = SettingsService.Load();
        bool changed = false;

        foreach (var threshold in _milestones)
        {
            if (followerCount < threshold) continue;
            var label = threshold.ToString();
            if (s.CelebratedMilestones.Contains(label)) continue;

            s.CelebratedMilestones.Add(label);
            changed = true;

            ShowToast(
                $"🎉  {threshold:N0} followers!",
                $"You hit {threshold:N0} followers! Congratulations — open Stream Command to celebrate.");
        }

        if (changed) SettingsService.Save(s);
    }

    // ── Live viewer chart polling ─────────────────────────────────────────────

    private void OnStreamStateChanged(bool isLive)
    {
        if (isLive)
        {
            _liveViewerHistory.Clear();
            _viewerPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _viewerPollTimer.Tick += async (_, _) => await PollViewerCountAsync();
            _viewerPollTimer.Start();

            // Show live preview card — Pro gate applied inside
            DashPreviewCard.Visibility = Visibility.Visible;
            ApplyDashPreviewProGate();
        }
        else
        {
            _viewerPollTimer?.Stop();
            _viewerPollTimer = null;
            _liveViewerHistory.Clear();
            DrawChart();   // revert to placeholder when offline

            // Stop preview and collapse card
            if (_dashPreviewHandler != null)
            {
                _ = VirtualCameraService.Instance.StopCaptureAsync(_dashPreviewHandler);
                _dashPreviewHandler = null;
            }
            DashPreviewCard.Visibility    = Visibility.Collapsed;
            DashPreviewImage.Source       = null;
            DashPreviewViewerCount.Text   = "—";
        }
    }

    private async void ApplyDashPreviewProGate()
    {
        bool isPro = FeatureGate.Has("live-preview");
        DashPreviewContent.Visibility = isPro ? Visibility.Visible  : Visibility.Collapsed;
        DashPreviewGate.Visibility    = isPro ? Visibility.Collapsed : Visibility.Visible;

        if (isPro && _dashPreviewHandler == null)
        {
            // Start the feed (no-op if device already open from LiveControl)
            _dashPreviewHandler = frame => DashPreviewImage.Source = frame;
            bool started = await VirtualCameraService.Instance.StartCaptureAsync(_dashPreviewHandler);
            if (!started)
            {
                // Virtual camera not found — silently hide the image area
                _dashPreviewHandler = null;
                DashPreviewContent.Visibility = Visibility.Collapsed;
            }
        }
        else if (!isPro && _dashPreviewHandler != null)
        {
            await VirtualCameraService.Instance.StopCaptureAsync(_dashPreviewHandler);
            _dashPreviewHandler = null;
        }
    }

    private async System.Threading.Tasks.Task PollViewerCountAsync()
    {
        var s = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(s.TwitchUsername) ||
            string.IsNullOrWhiteSpace(s.TwitchClientId)  ||
            string.IsNullOrWhiteSpace(s.TwitchChatToken)) return;

        var stats = await TwitchApiService.GetChannelStatsAsync(
            s.TwitchUsername, s.TwitchClientId, s.TwitchChatToken);
        if (stats == null) return;

        Dispatcher.Invoke(() =>
        {
            if (!stats.IsLive) return;

            LiveViewersText.Text        = stats.ViewerCount.ToString("N0");
            DashPreviewViewerCount.Text = stats.ViewerCount.ToString("N0");
            _liveViewerHistory.Add(stats.ViewerCount);
            if (_liveViewerHistory.Count > 20)
                _liveViewerHistory.RemoveAt(0);
            DrawChart();
        });
    }

    // ── Checklist card ────────────────────────────────────────────────────────

    private void UpdateChecklistCard(int done, int total)
    {
        double pct = total > 0 ? done * 100.0 / total : 0;
        ChecklistProgressBar.Value = pct;
        ChecklistBadgeText.Text    = total > 0 ? $"{done} / {total}" : "—";   // MATH 3: no "0 / 0"

        if (done == 0)
            ChecklistSubText.Text = "Open checklist to start your pre-stream setup";
        else if (done == total)
            ChecklistSubText.Text = "✓  All tasks complete — you're ready to go live!";
        else
            ChecklistSubText.Text = $"{total - done} task{(total - done == 1 ? "" : "s")} remaining before you go live";

        if (done == total && total > 0)
        {
            ChecklistBadge.Background     = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xC5, 0x5E));
            ChecklistBadge.BorderBrush    = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            ChecklistBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
        }
    }

    private void OpenChecklist_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("pre-stream");

    // ── OBS pill ──────────────────────────────────────────────────────────────

    private void UpdateOBSPill(bool isConnected)
    {
        var connectedColor    = Color.FromRgb(0x22, 0xC5, 0x5E);
        var disconnectedColor = Color.FromRgb(0xEF, 0x44, 0x44);
        var c     = isConnected ? connectedColor : disconnectedColor;
        var brush = new SolidColorBrush(c);

        OBSPill.BorderBrush    = brush;
        OBSPillDot.Fill        = brush;
        OBSPillText.Foreground = brush;
        OBSPillText.Text       = isConnected ? "OBS: Connected ✓" : "OBS: Disconnected";
        OBSPill.Background     = isConnected
            ? new SolidColorBrush(Color.FromArgb(0x20, 0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromArgb(0x20, 0xEF, 0x44, 0x44));
    }

    // ── Upcoming streams ──────────────────────────────────────────────────────

    private void BuildUpcomingList()
    {
        UpcomingList.Children.Clear();

        var s        = SettingsService.Load();
        var now      = DateTime.Now;
        var upcoming = s.PlannerEvents
                        .Where(e => e.When > now)
                        .OrderBy(e => e.When)
                        .Take(3)
                        .ToList();

        if (upcoming.Count == 0)
        {
            UpcomingList.Children.Add(new TextBlock
            {
                Text         = "No upcoming streams — add one in the Planner",
                Foreground   = (Brush)FindResource("MutedText"),
                FontSize     = 12,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var ev in upcoming)
        {
            var wrapper = new Border { Margin = new Thickness(0, 0, 0, 8) };
            var inner   = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Ellipse
            {
                Width  = 8, Height = 8,
                Fill   = ev.Platform == "YouTube"
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
                Margin            = new Thickness(0, 3, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text         = ev.Title,
                Foreground   = new SolidColorBrush(Colors.White),
                FontSize     = 13,
                TextWrapping = TextWrapping.Wrap
            });
            infoStack.Children.Add(new TextBlock
            {
                Text       = $"{ev.Platform}  ·  {ev.When:MMM d, h:mm tt}",
                Foreground = (Brush)FindResource("MutedText"),
                FontSize   = 11,
                Margin     = new Thickness(0, 2, 0, 0)
            });

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(infoStack, 1);
            inner.Children.Add(dot);
            inner.Children.Add(infoStack);
            wrapper.Child = inner;
            UpcomingList.Children.Add(wrapper);
        }
    }

    // ── Quick launch ──────────────────────────────────────────────────────────

    private void BuildQuickLaunch()
    {
        foreach (var (label, url, emoji) in _quickLinks)
        {
            var capturedUrl = url;
            var btn = new Button
            {
                Content = $"{emoji}  {label}",
                Style   = (Style)FindResource("SecondaryButton"),
                Margin  = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 8, 14, 8)
            };
            btn.Click += (_, _) => AppLaunchService.OpenUrl(capturedUrl);
            QuickLaunchPanel.Children.Add(btn);
        }

        var addBtn = new Button
        {
            Content = "+  Add App",
            Style   = (Style)FindResource("SecondaryButton"),
            Margin  = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14, 8, 14, 8)
        };
        addBtn.Click += (_, _) => MainWindow.NavigateTo?.Invoke("quick-launch");   // BUG 1: was dead
        QuickLaunchPanel.Children.Add(addBtn);
    }

    private void GoLive_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("live-control");

    // ── Viewer chart ──────────────────────────────────────────────────────────

    private void ViewerChart_Loaded(object sender, RoutedEventArgs e) => DrawChart();
    private void ViewerChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ViewerChart.Children.Clear();
        double w = ViewerChart.ActualWidth;
        double h = ViewerChart.ActualHeight;
        if (w < 10 || h < 10) return;

        // Use live history when available, fall back to placeholder
        int[] data = _liveViewerHistory.Count >= 2
            ? _liveViewerHistory.ToArray()
            : _placeholderData;

        int min   = Math.Max(0, data.Min() - 10);   // MATH 2: clamp to 0 — small channels never go negative
        int max   = data.Max() + 10;
        double range = Math.Max(1, max - min);
        int n = data.Length;

        var fillPoints = new PointCollection();
        var linePoints = new PointCollection();

        for (int i = 0; i < n; i++)
        {
            double x = i / (double)(n - 1) * w;
            double y = h - ((data[i] - min) / range * (h - 10)) - 5;
            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }
        fillPoints.Add(new Point(w, h));
        fillPoints.Add(new Point(0, h));

        ViewerChart.Children.Add(new Polygon
        {
            Points = fillPoints,
            Fill   = new LinearGradientBrush(
                Color.FromArgb(0x14, TC.Accent.R, TC.Accent.G, TC.Accent.B),
                Color.FromArgb(0x05, TC.Accent.R, TC.Accent.G, TC.Accent.B),
                new Point(0, 0), new Point(0, 1)),
            Stroke = Brushes.Transparent
        });

        ViewerChart.Children.Add(new Polyline
        {
            Points          = linePoints,
            Stroke          = new SolidColorBrush(TC.Accent),
            StrokeThickness = 1.5,
            StrokeLineJoin  = PenLineJoin.Round
        });

        var lastPt = linePoints[n - 1];
        var dot = new Ellipse
        {
            Width = 8, Height = 8,
            Fill   = new SolidColorBrush(TC.AccentLight),
            Stroke = new SolidColorBrush(TC.AppBg),
            StrokeThickness = 2
        };
        Canvas.SetLeft(dot, lastPt.X - 4);
        Canvas.SetTop(dot,  lastPt.Y - 4);
        ViewerChart.Children.Add(dot);
    }

    // ── Toast helper ─────────────────────────────────────────────────────────

    private static void ShowToast(string title, string body)
    {
        try
        {
            title = System.Security.SecurityElement.Escape(title);
            body  = System.Security.SecurityElement.Escape(body);

            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml($"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{title}</text>
                      <text>{body}</text>
                    </binding>
                  </visual>
                </toast>
                """);
            var toast = new Windows.UI.Notifications.ToastNotification(xml);
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier().Show(toast);
        }
        catch { }
    }
}
