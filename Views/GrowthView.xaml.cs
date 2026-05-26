using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamCommand.Models;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class GrowthView : UserControl
{
    private readonly ObservableCollection<GrowthGoal> _goals = new();

    private readonly List<(string Label, bool Achieved)> _milestones = new()
    {
        ("100 Followers",     true),
        ("500 Followers",     true),
        ("1,000 Followers",   true),
        ("5,000 Followers",   true),
        ("10,000 Followers",  true),
        ("15,000 Followers",  false),
        ("Twitch Affiliate",  true),
        ("Twitch Partner",    false),
        ("YouTube 1K Subs",   false),
    };

    public GrowthView()
    {
        InitializeComponent();

        // Seed default goals on first run
        var s = SettingsService.Load();
        if (s.GrowthGoals.Count == 0)
        {
            s.GrowthGoals.Add(new GrowthGoal { Label = "Followers",              Category = "Audience",    Current = 0,  Target = 1000, Unit = "followers" });
            s.GrowthGoals.Add(new GrowthGoal { Label = "Avg Concurrent Viewers", Category = "Audience",    Current = 0,  Target = 50,   Unit = "viewers"   });
            s.GrowthGoals.Add(new GrowthGoal { Label = "Subscribers",            Category = "Revenue",     Current = 0,  Target = 100,  Unit = "subs"      });
            s.GrowthGoals.Add(new GrowthGoal { Label = "Monthly Streams",        Category = "Consistency", Current = 0,  Target = 12,   Unit = "streams"   });
            s.GrowthGoals.Add(new GrowthGoal { Label = "Hours Streamed (Month)", Category = "Consistency", Current = 0,  Target = 40,   Unit = "hours"     });
            SettingsService.Save(s);
        }

        // BUG 4: load persisted goals
        foreach (var g in s.GrowthGoals)
            _goals.Add(g);

        GoalList.ItemsSource = _goals;
        BuildMilestones();
    }

    // ── Add Goal ─────────────────────────────────────────────────────────────

    private void AddGoal_Click(object sender, RoutedEventArgs e)
        => AddGoalForm.Visibility = Visibility.Visible;

    private void CancelGoal_Click(object sender, RoutedEventArgs e)
    {
        AddGoalForm.Visibility = Visibility.Collapsed;
        ClearGoalForm();
    }

    private void SaveGoal_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GoalLabelInput.Text)) return;

        double.TryParse(GoalCurrentInput.Text, out var current);
        double.TryParse(GoalTargetInput.Text, out var target);
        var category = (GoalCategoryInput.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Audience";

        var newGoal = new GrowthGoal
        {
            Label    = GoalLabelInput.Text.Trim(),
            Category = category,
            Current  = current,
            Target   = target > 0 ? target : 100,
            Unit     = string.IsNullOrWhiteSpace(GoalUnitInput.Text) ? "units" : GoalUnitInput.Text.Trim()
        };
        _goals.Add(newGoal);

        // BUG 4: persist goal so it survives restarts
        var s = SettingsService.Load();
        s.GrowthGoals.Add(newGoal);
        SettingsService.Save(s);

        AddGoalForm.Visibility = Visibility.Collapsed;
        ClearGoalForm();
    }

    private void ClearGoalForm()
    {
        GoalLabelInput.Text   = "";
        GoalCurrentInput.Text = "";
        GoalTargetInput.Text  = "";
        GoalUnitInput.Text    = "";
        GoalCategoryInput.SelectedIndex = 0;
    }

    // ── Milestones ────────────────────────────────────────────────────────────

    private void BuildMilestones()
    {
        foreach (var (label, achieved) in _milestones)
        {
            var border = new Border
            {
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(10, 8, 10, 8),
                Margin          = new Thickness(4),
                Background      = achieved
                    ? new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xC5, 0x5E))
                    : new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                BorderBrush     = achieved
                    ? new SolidColorBrush(Color.FromArgb(0x60, 0x22, 0xC5, 0x5E))
                    : (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Opacity         = achieved ? 1.0 : 0.55,
                Cursor          = achieved ? System.Windows.Input.Cursors.Hand : null,
                Tag             = label
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text              = achieved ? "✓" : "○",
                Foreground        = achieved ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedText"),
                FontSize          = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            });
            row.Children.Add(new TextBlock
            {
                Text              = label,
                Foreground        = achieved ? new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC)) : (Brush)FindResource("MutedText"),
                FontSize          = 12,
                FontWeight        = achieved ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping      = TextWrapping.Wrap
            });

            border.Child = row;

            // Tap achieved milestones to celebrate
            if (achieved)
                border.MouseLeftButtonUp += Milestone_Click;

            MilestoneGrid.Children.Add(border);
        }
    }

    private void Milestone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string label })
            ShowCelebration(label);
    }

    private void ShowCelebration(string label)
    {
        CelebrationTitle.Text   = $"🏆  {label} — Milestone hit!";
        CelebrationBanner.Visibility = Visibility.Visible;

        // Auto-dismiss after 4 seconds
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(4)
        };
        timer.Tick += (_, _) =>
        {
            CelebrationBanner.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    private void DismissCelebration_Click(object sender, RoutedEventArgs e)
        => CelebrationBanner.Visibility = Visibility.Collapsed;
}
