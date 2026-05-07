using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using StreamCommand.Models;

namespace StreamCommand.Views;

public class AutomationCategory
{
    public string Category { get; set; } = "";
    public ObservableCollection<AutomationRule> Rules { get; set; } = new();
}

public partial class AutomationView : UserControl
{
    public AutomationView()
    {
        InitializeComponent();

        var rules = new List<AutomationRule>
        {
            new() { Category="Followers",    Trigger="New follower",               Action="Send \"Thanks for the follow, @user! 🎉\"",                IsEnabled=true  },
            new() { Category="Subscribers",  Trigger="New subscriber",             Action="Play subscriber alert sound + on-screen animation",         IsEnabled=true  },
            new() { Category="Subscribers",  Trigger="Re-subscription (any tier)", Action="Send \"Welcome back @user! Month {months}! 🔥\"",           IsEnabled=true  },
            new() { Category="Raids",        Trigger="Raid received (10+ viewers)",Action="Send \"Welcome raiders from @raider! 🚀\"",                  IsEnabled=false },
            new() { Category="Bits",         Trigger="Bits cheer (100+)",          Action="Play cheer sound + show on-screen effect",                  IsEnabled=true  },
            new() { Category="Stream Events",Trigger="Stream starts",              Action="Post in Discord #stream-announcements",                     IsEnabled=false },
            new() { Category="Stream Events",Trigger="Stream ends",                Action="Send \"Thanks for watching! See you next time 👋\"",         IsEnabled=true  },
            new() { Category="Commands",     Trigger="!socials in chat",           Action="Post links to Twitter, YouTube, Discord",                   IsEnabled=true  },
            new() { Category="Commands",     Trigger="!schedule in chat",          Action="Post upcoming stream schedule",                             IsEnabled=true  },
            new() { Category="Milestones",   Trigger="Viewer count hits 100",      Action="Celebrate milestone in chat",                              IsEnabled=false },
        };

        var categories = rules
            .GroupBy(r => r.Category)
            .Select(g => new AutomationCategory { Category = g.Key, Rules = new(g) })
            .ToList();

        CategoriesControl.ItemsSource = categories;
    }
}
