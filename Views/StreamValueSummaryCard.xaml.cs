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

        if (s.StreamsCompleted >= 5)
        {
            HeadlineText.Text = $"You've completed {s.StreamsCompleted} streams with Stream Command!";
            SubText.Text      = "Pro unlocks advanced analytics, unlimited automation, live preview, and more — everything that turns good streams into great ones.";
        }
        else if (s.StreamsCompleted >= 3)
        {
            HeadlineText.Text = "You're on a roll — 3 streams and counting!";
            SubText.Text      = "Upgrade to Pro and unlock live preview, advanced chat automation, and priority stats — built for streamers who are serious about growing.";
        }
        else if (s.AutomationFiredCount >= 5)
        {
            HeadlineText.Text = $"Your automations have fired {s.AutomationFiredCount} times already!";
            SubText.Text      = "With Pro you can build unlimited automation rules and schedule them around your stream — your chat engagement on autopilot.";
        }
        else
        {
            HeadlineText.Text = "Stream Command is working hard for you.";
            SubText.Text      = "Upgrade to Pro to unlock every feature — live preview, unlimited automation, advanced analytics, and more.";
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
