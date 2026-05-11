using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class DashboardView : UserControl
{
    private readonly int[] _viewerData = { 280, 295, 310, 302, 318, 308, 325, 305, 315, 300, 320 };

    private readonly List<(string Title, string Platform, string Date)> _upcoming = new()
    {
        ("Ranked Grind — Road to Diamond", "Twitch", "May 8, 7:00 PM"),
        ("Friday Night Warzone with Subs",  "Twitch", "May 9, 8:00 PM"),
        ("Chill Minecraft Building Session","YouTube","May 10, 3:00 PM")
    };

    private readonly List<(string Label, string Url, string Emoji)> _quickLinks = new()
    {
        ("Twitch Dashboard",  "https://dashboard.twitch.tv",              "🟣"),
        ("YouTube Studio",    "https://studio.youtube.com",               "🔴"),
        ("Discord Web",       "https://discord.com/app",                  "💬"),
        ("Pretzel Music",     "https://www.pretzel.rocks",                "🎵"),
        ("StreamElements",    "https://streamelements.com/dashboard",     "🟠")
    };

    public DashboardView()
    {
        InitializeComponent();
        BuildUpcomingList();
        BuildQuickLaunch();

        // Subscribe to OBS state changes from LiveControlView
        StreamEvents.OBSStateChanged += isConnected =>
            Dispatcher.Invoke(() => UpdateOBSPill(isConnected));

        // Subscribe to checklist progress changes from PreStreamView
        StreamEvents.ChecklistProgressChanged += (done, total) =>
            Dispatcher.Invoke(() => UpdateChecklistCard(done, total));
    }

    private void UpdateChecklistCard(int done, int total)
    {
        double pct = total > 0 ? done * 100.0 / total : 0;
        ChecklistProgressBar.Value  = pct;
        ChecklistBadgeText.Text     = $"{done} / {total}";

        if (done == 0)
            ChecklistSubText.Text = "Open checklist to start your pre-stream setup";
        else if (done == total)
            ChecklistSubText.Text = "✓  All tasks complete — you're ready to go live!";
        else
            ChecklistSubText.Text = $"{total - done} task{(total - done == 1 ? "" : "s")} remaining before you go live";

        // Badge turns green when complete
        if (done == total && total > 0)
        {
            ChecklistBadge.Background  = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xC5, 0x5E));
            ChecklistBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            ChecklistBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
        }
    }

    private void OpenChecklist_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("pre-stream");

    private void UpdateOBSPill(bool isConnected)
    {
        var connectedColor = Color.FromRgb(0x22, 0xC5, 0x5E);
        var disconnectedColor = Color.FromRgb(0xEF, 0x44, 0x44);
        var c = isConnected ? connectedColor : disconnectedColor;
        var brush = new SolidColorBrush(c);

        OBSPill.BorderBrush = brush;
        OBSPillDot.Fill = brush;
        OBSPillText.Foreground = brush;
        OBSPillText.Text = isConnected ? "OBS: Connected ✓" : "OBS: Disconnected";
        OBSPill.Background = isConnected
            ? new SolidColorBrush(Color.FromArgb(0x20, 0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromArgb(0x20, 0xEF, 0x44, 0x44));
    }

    private void BuildUpcomingList()
    {
        foreach (var (title, platform, date) in _upcoming)
        {
            var wrapper = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(0, 0, 0, 8) };
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dotPanel = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = platform == "YouTube"
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
                Margin = new Thickness(0, 3, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{platform}  ·  {date}",
                Foreground = (Brush)FindResource("MutedText"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });

            Grid.SetColumn(dotPanel, 0);
            Grid.SetColumn(infoStack, 1);
            inner.Children.Add(dotPanel);
            inner.Children.Add(infoStack);
            wrapper.Child = inner;
            UpcomingList.Children.Add(wrapper);
        }
    }

    private void BuildQuickLaunch()
    {
        foreach (var (label, url, emoji) in _quickLinks)
        {
            var capturedUrl = url;
            var btn = new Button
            {
                Content = $"{emoji}  {label}",
                Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 8, 14, 8)
            };
            btn.Click += (_, _) => AppLaunchService.OpenUrl(capturedUrl);
            QuickLaunchPanel.Children.Add(btn);
        }

        var addBtn = new Button
        {
            Content = "+  Add App",
            Style = (Style)FindResource("SecondaryButton"),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14, 8, 14, 8)
        };
        QuickLaunchPanel.Children.Add(addBtn);
    }

    private void GoLive_Click(object sender, RoutedEventArgs e)
        => MainWindow.NavigateTo?.Invoke("live-control");

    private void ViewerChart_Loaded(object sender, RoutedEventArgs e) => DrawChart();
    private void ViewerChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ViewerChart.Children.Clear();
        double w = ViewerChart.ActualWidth;
        double h = ViewerChart.ActualHeight;
        if (w < 10 || h < 10) return;

        int min = 260, max = 340;
        double range = max - min;
        int n = _viewerData.Length;

        var fillPoints = new PointCollection();
        var linePoints = new PointCollection();

        for (int i = 0; i < n; i++)
        {
            double x = i / (double)(n - 1) * w;
            double y = h - ((_viewerData[i] - min) / range * (h - 10)) - 5;
            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }
        fillPoints.Add(new Point(w, h));
        fillPoints.Add(new Point(0, h));

        // Gradient fill
        var fill = new Polygon
        {
            Points = fillPoints,
            Fill = new LinearGradientBrush(
                Color.FromArgb(70, 0x7C, 0x3A, 0xED),
                Color.FromArgb(5, 0x7C, 0x3A, 0xED),
                new Point(0, 0), new Point(0, 1)),
            Stroke = Brushes.Transparent
        };

        // Line
        var line = new Polyline
        {
            Points = linePoints,
            Stroke = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };

        // Dot at last point
        var lastPt = linePoints[n - 1];
        var dot = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            StrokeThickness = 2
        };
        Canvas.SetLeft(dot, lastPt.X - 4);
        Canvas.SetTop(dot, lastPt.Y - 4);

        ViewerChart.Children.Add(fill);
        ViewerChart.Children.Add(line);
        ViewerChart.Children.Add(dot);
    }
}
