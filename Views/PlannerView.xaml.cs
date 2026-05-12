using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StreamCommand.Models;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class PlannerView : UserControl
{
    private readonly ObservableCollection<StreamEvent> _events = new();

    public PlannerView()
    {
        InitializeComponent();
        EventList.ItemsSource = _events;
        LoadEvents();
        RefreshFreeBanner();

        // Re-evaluate Pro gate after entitlement is confirmed by the Store
        EntitlementService.Refreshed += () => Dispatcher.Invoke(RefreshFreeBanner);
    }

    private void RefreshFreeBanner()
    {
        bool atLimit = !FeatureGate.Has("planner-unlimited") && _events.Count >= 3;
        PlannerFreeBanner.Visibility = atLimit ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool hasUpcoming = _events.Any(e => !e.IsPast);
        EmptyState.Visibility = hasUpcoming ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void LoadEvents()
    {
        var s = SettingsService.Load();
        _events.Clear();

        if (s.PlannerEvents.Count == 0)
        {
            // Seed with future-relative example data so the view is never empty on first run
            var today = DateTime.Today;
            _events.Add(new StreamEvent { Title = "Ranked Grind — Road to Diamond",   Platform = "Twitch",  StreamDateTime = today.AddDays(2).AddHours(19), Duration = "3h", Notes = "Focus on support play" });
            _events.Add(new StreamEvent { Title = "Friday Night Warzone with Subs",   Platform = "Twitch",  StreamDateTime = today.AddDays(4).AddHours(20), Duration = "4h", Notes = "Sub games night" });
            _events.Add(new StreamEvent { Title = "Chill Minecraft Building Session", Platform = "YouTube", StreamDateTime = today.AddDays(6).AddHours(15), Duration = "2h", Notes = "Sky island build" });
            SaveEvents();
        }
        else
        {
            foreach (var ev in s.PlannerEvents)
                _events.Add(new StreamEvent
                {
                    Title          = ev.Title,
                    Platform       = ev.Platform,
                    StreamDateTime = ev.When,
                    Duration       = ev.Duration,
                    Notes          = ev.Notes
                });
        }

        SortEvents();
        UpdateEmptyState();
    }

    private void SaveEvents()
    {
        var s = SettingsService.Load();
        s.PlannerEvents = _events.Select(e => new PlannerEvent
        {
            Title    = e.Title,
            Platform = e.Platform,
            When     = e.StreamDateTime,
            Duration = e.Duration,
            Notes    = e.Notes
        }).ToList();
        SettingsService.Save(s);

        // Notify Dashboard so its upcoming list stays in sync
        StreamEvents.RaisePlannerChanged();
    }

    /// <summary>
    /// Sorts _events in place: upcoming (ascending by date) first, past (descending) at the bottom.
    /// </summary>
    private void SortEvents()
    {
        var sorted = _events
            .OrderBy(e => e.IsPast)                          // upcoming (false) before past (true)
            .ThenBy(e => e.IsPast  ? DateTime.MinValue : e.StreamDateTime)   // upcoming: ascending
            .ThenByDescending(e => !e.IsPast ? DateTime.MinValue : e.StreamDateTime)  // past: descending
            .ToList();

        _events.Clear();
        foreach (var ev in sorted)
            _events.Add(ev);
    }

    // ── Add / Cancel / Save ──────────────────────────────────────────────────

    private void AddStream_Click(object sender, RoutedEventArgs e)
    {
        if (!FeatureGate.Has("planner-unlimited") && _events.Count >= 3)
        {
            var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
            win.ShowDialog();
            return;
        }
        AddForm.Visibility = Visibility.Visible;
    }

    private void CancelAdd_Click(object sender, RoutedEventArgs e)
        => AddForm.Visibility = Visibility.Collapsed;

    private void SaveStream_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleInput.Text)) return;

        if (!FeatureGate.Has("planner-unlimited") && _events.Count >= 3)
        {
            AddForm.Visibility = Visibility.Collapsed;
            var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
            win.ShowDialog();
            return;
        }

        var date     = DateInput.SelectedDate ?? DateTime.Now.AddDays(1);
        var platform = (PlatformInput.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Twitch";

        if (!TimeSpan.TryParse(TimeInput.Text, out var time))
            time = new TimeSpan(19, 0, 0);

        _events.Add(new StreamEvent
        {
            Title          = TitleInput.Text,
            Platform       = platform,
            StreamDateTime = date.Date + time,
            Duration       = "2h",
            Notes          = NotesInput.Text
        });

        SortEvents();
        SaveEvents();
        RefreshFreeBanner();

        TitleInput.Text    = "";
        TimeInput.Text     = "";
        NotesInput.Text    = "";
        AddForm.Visibility = Visibility.Collapsed;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StreamEvent ev })
        {
            _events.Remove(ev);
            SaveEvents();
            RefreshFreeBanner();
        }
    }
}
