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
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void LoadEvents()
    {
        var s = SettingsService.Load();
        _events.Clear();

        if (s.PlannerEvents.Count == 0)
        {
            // First run — seed with example data so the view isn't empty
            _events.Add(new StreamEvent { Title = "Ranked Grind — Road to Diamond",   Platform = "Twitch",  StreamDateTime = new DateTime(2026, 5, 8, 19, 0, 0), Duration = "3h", Notes = "Focus on support play" });
            _events.Add(new StreamEvent { Title = "Friday Night Warzone with Subs",   Platform = "Twitch",  StreamDateTime = new DateTime(2026, 5, 9, 20, 0, 0), Duration = "4h", Notes = "Sub games night" });
            _events.Add(new StreamEvent { Title = "Chill Minecraft Building Session", Platform = "YouTube", StreamDateTime = new DateTime(2026, 5,10, 15, 0, 0), Duration = "2h", Notes = "Sky island build" });
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
    }

    // ── Add / Cancel / Save ──────────────────────────────────────────────────

    private void AddStream_Click(object sender, RoutedEventArgs e)
        => AddForm.Visibility = Visibility.Visible;

    private void CancelAdd_Click(object sender, RoutedEventArgs e)
        => AddForm.Visibility = Visibility.Collapsed;

    private void SaveStream_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleInput.Text)) return;

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

        SaveEvents();   // persist immediately

        TitleInput.Text  = "";
        TimeInput.Text   = "";
        NotesInput.Text  = "";
        AddForm.Visibility = Visibility.Collapsed;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StreamEvent ev })
        {
            _events.Remove(ev);
            SaveEvents();   // persist immediately
        }
    }
}
