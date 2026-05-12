using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class AnalyticsView : UserControl
{
    private readonly double[] _followers   = { 12100, 12340, 12520, 12700, 12800, 12894 };
    private readonly double[] _avgViewers  = { 280, 310, 295, 320, 305 };
    private readonly double[] _peakViewers = { 340, 390, 360, 410, 380 };
    private readonly double[] _hours       = { 3.5, 4.0, 2.5, 5.0, 3.0 };
    private readonly string[] _streamDates = { "Apr 28", "Apr 30", "May 1", "May 3", "May 5" };

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(60) };

    public AnalyticsView()
    {
        InitializeComponent();
        _refreshTimer.Tick += async (_, _) => await LoadLiveDataAsync();
        Loaded += async (_, _) =>
        {
            ApplyProGate();
            await LoadLiveDataAsync();
            _refreshTimer.Start();

            // Re-evaluate Pro gate when Store confirms entitlement
            EntitlementService.Refreshed += () => Dispatcher.Invoke(ApplyProGate);
        };
    }

    private void ApplyProGate()
    {
        bool isPro = FeatureGate.Has("analytics-history");
        ProChartsGate.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
        ChartsContent.Effect     = isPro ? null : new BlurEffect { Radius = 10, KernelType = KernelType.Gaussian };
    }

    private async System.Threading.Tasks.Task LoadLiveDataAsync()
    {
        var s = SettingsService.Load();

        if (string.IsNullOrWhiteSpace(s.TwitchUsername) ||
            string.IsNullOrWhiteSpace(s.TwitchClientId)  ||
            string.IsNullOrWhiteSpace(s.TwitchChatToken))
        {
            ApiStatusBanner.Visibility = Visibility.Visible;
            ApiStatusText.Text = "ℹ  Live stats require Twitch credentials";
            ApiHintText.Visibility = Visibility.Visible;
            return;
        }

        ApiStatusBanner.Visibility = Visibility.Visible;
        ApiStatusText.Text  = "🔄  Refreshing…";
        ApiHintText.Visibility = Visibility.Collapsed;

        var stats = await TwitchApiService.GetChannelStatsAsync(
            s.TwitchUsername, s.TwitchClientId, s.TwitchChatToken);

        if (stats is null)
        {
            ApiStatusText.Text = "⚠  Could not reach Twitch API — check your credentials in Settings";
            return;
        }

        // Viewer count
        if (stats.IsLive)
        {
            StatViewers.Text    = stats.ViewerCount.ToString("N0");
            StatViewersSub.Text = "Live right now";
            StatViewersSub.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        }
        else
        {
            StatViewers.Text    = "—";
            StatViewersSub.Text = "Not currently live";
            StatViewersSub.Foreground = (System.Windows.Media.Brush)FindResource("MutedText");
        }

        // Followers
        if (stats.FollowerCount >= 0)
        {
            StatFollowers.Text    = stats.FollowerCount.ToString("N0");
            StatFollowersSub.Text = "Total followers";
        }
        else
        {
            StatFollowers.Text    = "—";
            StatFollowersSub.Text = "Needs moderator scope";
        }

        // Game
        StatGame.Text    = stats.IsLive && !string.IsNullOrEmpty(stats.GameName) ? stats.GameName : "—";
        StatGameSub.Text = stats.IsLive ? "Current category" : "Offline";

        // Status
        StatStatus.Text       = stats.IsLive ? "🔴 LIVE" : "Offline";
        StatStatus.Foreground = stats.IsLive
            ? (System.Windows.Media.Brush)FindResource("LiveBrush")
            : (System.Windows.Media.Brush)FindResource("MutedText");

        ApiStatusText.Text     = $"✓  Last updated {DateTime.Now:h:mm tt}";
        ApiHintText.Visibility = Visibility.Collapsed;
    }

    // ── Follower line chart ─────────────────────────────────────────────────
    private void FollowerChart_Loaded(object s, RoutedEventArgs e) => DrawFollowers();
    private void FollowerChart_SizeChanged(object s, SizeChangedEventArgs e) => DrawFollowers();

    private void DrawFollowers()
    {
        FollowerChart.Children.Clear();
        double w = FollowerChart.ActualWidth, h = FollowerChart.ActualHeight;
        if (w < 10 || h < 10) return;

        double min = 11900, max = 13100, range = max - min;
        var pts = new PointCollection();
        for (int i = 0; i < _followers.Length; i++)
        {
            double x = i / (double)(_followers.Length - 1) * w;
            double y = h - (_followers[i] - min) / range * (h - 10) - 5;
            pts.Add(new Point(x, y));
        }
        var fill = new PointCollection(pts) { new(w, h), new(0, h) };
        FollowerChart.Children.Add(new Polygon
        {
            Points = fill,
            Fill = new LinearGradientBrush(Color.FromArgb(70, 0x7C, 0x3A, 0xED),
                                           Color.FromArgb(5,  0x7C, 0x3A, 0xED),
                                           new Point(0,0), new Point(0,1)),
            Stroke = Brushes.Transparent
        });
        FollowerChart.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round
        });
    }

    // ── Avg vs Peak bar chart ──────────────────────────────────────────────
    private void ViewerBarChart_Loaded(object s, RoutedEventArgs e) => DrawViewerBars();
    private void ViewerBarChart_SizeChanged(object s, SizeChangedEventArgs e) => DrawViewerBars();

    private void DrawViewerBars()
    {
        ViewerBarChart.Children.Clear();
        double w = ViewerBarChart.ActualWidth, h = ViewerBarChart.ActualHeight;
        if (w < 10 || h < 10) return;

        int n = _avgViewers.Length;
        double groupW = w / n;
        double barW   = groupW * 0.30;
        double maxVal = 450;

        for (int i = 0; i < n; i++)
        {
            double groupX = i * groupW;
            DrawBar(ViewerBarChart, groupX + groupW * 0.10, barW, h, _avgViewers[i],  maxVal, Color.FromRgb(0x7C,0x3A,0xED));
            DrawBar(ViewerBarChart, groupX + groupW * 0.48, barW, h, _peakViewers[i], maxVal, Color.FromRgb(0x4C,0x1D,0x95));

            ViewerBarChart.Children.Add(new TextBlock
            {
                Text = _streamDates[i],
                Foreground = (Brush)FindResource("MutedText"),
                FontSize = 10
            });
            var tb = (TextBlock)ViewerBarChart.Children[^1];
            Canvas.SetLeft(tb, groupX + groupW * 0.10);
            Canvas.SetTop(tb, h - 14);
        }
    }

    // ── Hours bar chart ────────────────────────────────────────────────────
    private void HoursBarChart_Loaded(object s, RoutedEventArgs e) => DrawHoursBars();
    private void HoursBarChart_SizeChanged(object s, SizeChangedEventArgs e) => DrawHoursBars();

    private void DrawHoursBars()
    {
        HoursBarChart.Children.Clear();
        double w = HoursBarChart.ActualWidth, h = HoursBarChart.ActualHeight;
        if (w < 10 || h < 10) return;

        int n = _hours.Length;
        double groupW = w / n;
        double barW   = groupW * 0.50;

        for (int i = 0; i < n; i++)
        {
            double groupX = i * groupW;
            DrawBar(HoursBarChart, groupX + groupW * 0.25, barW, h, _hours[i], 6, Color.FromRgb(0xA7,0x8B,0xFA));

            HoursBarChart.Children.Add(new TextBlock
            {
                Text = _streamDates[i],
                Foreground = (Brush)FindResource("MutedText"),
                FontSize = 10
            });
            var tb = (TextBlock)HoursBarChart.Children[^1];
            Canvas.SetLeft(tb, groupX + groupW * 0.10);
            Canvas.SetTop(tb, h - 14);
        }
    }

    private static void DrawBar(Canvas canvas, double x, double bw, double ch, double value, double max, Color color)
    {
        double barH = value / max * (ch - 20);
        double top  = ch - barH - 16;
        var rect = new Rectangle
        {
            Width = bw, Height = Math.Max(2, barH),
            RadiusX = 4, RadiusY = 4,
            Fill = new SolidColorBrush(color)
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, top);
        canvas.Children.Add(rect);
    }
}
