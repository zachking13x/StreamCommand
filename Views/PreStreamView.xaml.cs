using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StreamCommand.Models;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class PreStreamView : UserControl
{
    private readonly ObservableCollection<ChecklistSection> _sections = new();

    public PreStreamView()
    {
        InitializeComponent();
        BuildChecklist();
        SectionsControl.ItemsSource = _sections;
        RefreshProgress();
    }

    private void BuildChecklist()
    {
        _sections.Add(new ChecklistSection
        {
            Title = "Launch Apps", Icon = "🚀",
            Items = new()
            {
                new() { Label = "Launch OBS Studio (installed on your PC)",
                         Description = "Open OBS from your desktop or taskbar — cannot be launched from a browser" },
                new() { Label = "Open Twitch / YouTube Dashboard",
                         Description = "Access your stream manager in the browser",
                         Links = [ new() { Label = "Twitch Dashboard ↗", Url = "https://dashboard.twitch.tv" },
                                   new() { Label = "YouTube Studio ↗",   Url = "https://studio.youtube.com" } ] },
                new() { Label = "Open Discord",
                         Description = "Opens Discord web — or switch to your desktop app manually",
                         Links = [ new() { Label = "Discord Web ↗", Url = "https://discord.com/app" } ] },
                new() { Label = "Start DMCA-Safe Music",
                         Description = "Opens in browser — play via your desktop app or browser tab",
                         Links = [ new() { Label = "Pretzel Rocks ↗", Url = "https://www.pretzel.rocks" },
                                   new() { Label = "Monstercat ↗",    Url = "https://www.monstercat.com/player" } ] },
                new() { Label = "Open StreamElements / Streamlabs",
                         Description = "Opens dashboard in browser",
                         Links = [ new() { Label = "StreamElements ↗", Url = "https://streamelements.com/dashboard" },
                                   new() { Label = "Streamlabs ↗",     Url = "https://streamlabs.com/dashboard" } ] }
            }
        });

        _sections.Add(new ChecklistSection
        {
            Title = "Scene & Audio Setup", Icon = "🎬",
            Items = new()
            {
                new() { Label = "Load Starting Soon scene",   Description = "Set OBS to your starting scene before going live" },
                new() { Label = "Check mic levels",           Description = "Speak and confirm levels are in the green zone in OBS" },
                new() { Label = "Check desktop audio",        Description = "Play music/game audio and confirm it's captured in OBS" },
                new() { Label = "Virtual audio devices active", Description = "VoiceMeeter or equivalent routing is running" }
            }
        });

        _sections.Add(new ChecklistSection
        {
            Title = "Video & Camera", Icon = "📷",
            Items = new()
            {
                new() { Label = "Check webcam",          Description = "Preview is showing, lighting is good, background is clean" },
                new() { Label = "Game capture is working", Description = "Game scene visible in OBS preview" },
                new() { Label = "Overlays & alerts active", Description = "Browser sources are loaded in OBS" }
            }
        });

        _sections.Add(new ChecklistSection
        {
            Title = "Stream Settings", Icon = "⚙",
            Items = new()
            {
                new() { Label = "Set stream title & category",  Description = "Update on Twitch/YouTube before going live",
                         Links = [ new() { Label = "Twitch Dashboard ↗", Url = "https://dashboard.twitch.tv" } ] },
                new() { Label = "Confirm bitrate & quality",    Description = "Check OBS output settings match your upload speed" },
                new() { Label = "Run a quick test stream",      Description = "Stream to a private channel for 1 minute and review" }
            }
        });

        _sections.Add(new ChecklistSection
        {
            Title = "Social & Community", Icon = "📣",
            Items = new()
            {
                new() { Label = "Post 'Going Live' announcement", Description = "Tweet, post, or message your community",
                         Links = [ new() { Label = "Twitter/X ↗", Url = "https://twitter.com/compose/tweet" } ] },
                new() { Label = "Post in Discord", Description = "Announce in your Discord server's stream channel",
                         Links = [ new() { Label = "Discord Web ↗", Url = "https://discord.com/app" } ] },
                new() { Label = "Check Twitch schedule is current", Description = "Verify upcoming streams are correctly listed",
                         Links = [ new() { Label = "Twitch Schedule ↗", Url = "https://dashboard.twitch.tv/schedule" } ] }
            }
        });

        _sections.Add(new ChecklistSection
        {
            Title = "Final Checks", Icon = "✅",
            Items = new()
            {
                new() { Label = "Water / snacks ready",     Description = "Stay hydrated for the stream" },
                new() { Label = "Enable Do Not Disturb",    Description = "Silence phone and disable notifications on PC" },
                new() { Label = "Lighting is set",          Description = "Key light, fill light, and back light are on and positioned" }
            }
        });

        // Subscribe to all item changes for progress tracking
        foreach (var section in _sections)
            foreach (var item in section.Items)
                item.PropertyChanged += (_, _) => RefreshProgress();
    }

    private void RefreshProgress()
    {
        var all   = _sections.SelectMany(s => s.Items).ToList();
        var done  = all.Count(i => i.IsChecked);
        var total = all.Count;
        double pct = total > 0 ? done * 100.0 / total : 0;

        ProgressBar.Value    = pct;
        ProgressLabel.Text   = $"{done} / {total} tasks complete";
        ProgressPct.Text     = $"{pct:F0}%";

        // Broadcast to Dashboard summary card
        StreamEvents.RaiseChecklistProgress(done, total);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _sections.SelectMany(s => s.Items))
            item.IsChecked = false;
    }

    private void SectionHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ChecklistSection section)
            section.IsExpanded = !section.IsExpanded;
    }

    private void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url)
            AppLaunchService.OpenUrl(url);
    }
}
