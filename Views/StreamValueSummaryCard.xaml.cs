using StreamCommand.Services;
using System.Windows;
using System.Windows.Controls;

namespace StreamCommand.Views;

public partial class StreamValueSummaryCard : UserControl
{
    public event Action? Dismissed;

    public StreamValueSummaryCard()
    {
        InitializeComponent();
        Loaded += (_, _) => Populate();
    }

    private void Populate()
    {
        var s = SettingsService.Load();

        // Lead with the want (live preview) first; fall back to the limit (automation cap) second.
        if (s.StreamsCompleted >= 3)
        {
            HeadlineText.Text = $"You've streamed {s.StreamsCompleted} times — unlock live preview";
            SubText.Text      = "See your stream live inside Stream Command and stop alt-tabbing to check your scene. Upgrade to Pro.";
        }
        else if (s.AutomationFiredCount >= 5)
        {
            HeadlineText.Text = $"Your automations fired {s.AutomationFiredCount} times — go unlimited";
            SubText.Text      = "You're running one free rule. Upgrade to Pro to automate every follower, sub, and raid at once.";
        }
        else
        {
            HeadlineText.Text = "Take full control of your stream";
            SubText.Text      = "Upgrade to Pro to unlock live preview, on-stream alerts, and unlimited automation — run your whole broadcast from one window.";
        }
    }

    private void CtaButton_Click(object sender, RoutedEventArgs e)
    {
        var s   = SettingsService.Load();
        var ctx = new UsageContext
        {
            StreamsCompleted     = s.StreamsCompleted,
            AutomationFiredCount = s.AutomationFiredCount,
            ProGateHitCount      = s.ProGateHitCount,
        };
        var win = new ProUpgradeWindow(ctx) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Load();
        s.StreamValueCardDismissed = true;
        SettingsService.Save(s);
        Dismissed?.Invoke();
    }
}
