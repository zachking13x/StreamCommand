using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class StreamAlertOverlay : Window
{
    public StreamAlertOverlay()
    {
        InitializeComponent();

        // Subscribe to the shared event bus
        StreamEvents.AlertFired += OnAlertFired;

        // Position in bottom-right corner of the primary screen
        Loaded += (_, _) => RepositionToBottomRight();
        SystemParameters.StaticPropertyChanged += (_, _) => RepositionToBottomRight();
    }

    private void RepositionToBottomRight()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Right  - Width  - 16;
        Top  = screen.Bottom - Height - 16;
    }

    private void OnAlertFired(string emoji, string message)
    {
        // Must marshal back to UI thread since events can fire from background threads
        Dispatcher.Invoke(() => ShowToast(emoji, message));
    }

    private void ShowToast(string emoji, string message)
    {
        // Build toast card
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x0B, 0x35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 0),
            Opacity = 0
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = emoji,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        row.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 260
        });

        card.Child = row;
        AlertStack.Children.Add(card);

        // Reposition after layout updates
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)RepositionToBottomRight);

        // Fade in
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
        card.BeginAnimation(OpacityProperty, fadeIn);

        // Auto-dismiss after 5 seconds
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fadeOut.Completed += (_, _) =>
            {
                AlertStack.Children.Remove(card);
                RepositionToBottomRight();
            };
            card.BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        StreamEvents.AlertFired -= OnAlertFired;
        base.OnClosed(e);
    }
}
