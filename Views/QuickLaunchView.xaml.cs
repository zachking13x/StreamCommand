using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StreamCommand.Models;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class QuickLaunchView : UserControl
{
    private readonly List<QuickLaunchItem> _apps = new()
    {
        new() { Label="Twitch Dashboard",  Url="https://dashboard.twitch.tv",             Emoji="🟣", Category="Streaming"         },
        new() { Label="YouTube Studio",    Url="https://studio.youtube.com",              Emoji="🔴", Category="Streaming"         },
        new() { Label="StreamElements",    Url="https://streamelements.com/dashboard",    Emoji="🟠", Category="Overlays & Alerts" },
        new() { Label="Streamlabs",        Url="https://streamlabs.com/dashboard",        Emoji="🎮", Category="Overlays & Alerts" },
        new() { Label="Discord Web",       Url="https://discord.com/app",                Emoji="💬", Category="Community"         },
        new() { Label="Twitter / X",       Url="https://twitter.com",                    Emoji="🐦", Category="Community"         },
        new() { Label="Pretzel Rocks",     Url="https://www.pretzel.rocks",              Emoji="🎵", Category="Music"             },
        new() { Label="Monstercat",        Url="https://www.monstercat.com/player",      Emoji="🐱", Category="Music"             },
        new() { Label="Canva",             Url="https://www.canva.com",                  Emoji="🎨", Category="Design"            },
        new() { Label="Twitch Analytics",  Url="https://dashboard.twitch.tv/analytics",  Emoji="📊", Category="Analytics"         },
        new() { Label="Social Blade",      Url="https://socialblade.com",                Emoji="📈", Category="Analytics"         },
        new() { Label="OBS Project",       Url="https://obsproject.com",                 Emoji="🎬", Category="Tools"             },
    };

    private string _activeCategory = "All";

    public QuickLaunchView()
    {
        InitializeComponent();

        // BUG 2: load persisted custom apps on top of the hardcoded defaults
        var s = SettingsService.Load();
        foreach (var custom in s.CustomQuickLaunchItems)
            _apps.Add(custom);

        BuildCategoryFilters();
        RenderTiles();
    }

    private void BuildCategoryFilters()
    {
        CategoryFilters.Children.Clear();
        var cats = new[] { "All" }.Concat(_apps.Select(a => a.Category).Distinct());

        foreach (var cat in cats)
        {
            var c = cat;
            var btn = new Button
            {
                Content = cat,
                Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 12
            };
            if (cat == _activeCategory)
            {
                btn.Background  = new SolidColorBrush(Color.FromArgb(0x30, 0x6B, 0x9E, 0x85));   // sage tint
                btn.Foreground  = (Brush)FindResource("AccentLight");
                btn.BorderBrush = (Brush)FindResource("AccentBorder");
            }
            btn.Click += (_, _) => { _activeCategory = c; BuildCategoryFilters(); RenderTiles(); };
            CategoryFilters.Children.Add(btn);
        }
    }

    private void RenderTiles()
    {
        AppTiles.Children.Clear();
        var filtered = _activeCategory == "All"
            ? _apps
            : _apps.Where(a => a.Category == _activeCategory).ToList();

        foreach (var app in filtered)
        {
            var a = app;
            var tile = new Border
            {
                Width = 160, Height = 110,
                Margin = new Thickness(0, 0, 10, 10),
                Background = (Brush)FindResource("CardBackground"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();
            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            topRow.Children.Add(new TextBlock { Text = app.Emoji, FontSize = 22 });
            stack.Children.Add(topRow);
            stack.Children.Add(new TextBlock
            {
                Text = app.Label,
                Foreground = (Brush)FindResource("PrimaryText"),
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = app.Category,
                Foreground = (Brush)FindResource("MutedText"),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            });
            tile.Child = stack;

            tile.MouseEnter += (_, _) =>
            {
                tile.BorderBrush = (Brush)FindResource("AccentBorder");
                tile.Background  = (Brush)FindResource("HoverBackground");
            };
            tile.MouseLeave += (_, _) =>
            {
                tile.BorderBrush = (Brush)FindResource("BorderBrush");
                tile.Background = (Brush)FindResource("CardBackground");
            };
            tile.MouseLeftButtonUp += (_, _) => AppLaunchService.OpenUrl(a.Url);

            AppTiles.Children.Add(tile);
        }
    }

    private void ShowAddForm_Click(object sender, RoutedEventArgs e)
        => AddForm.Visibility = Visibility.Visible;

    private void CancelAdd_Click(object sender, RoutedEventArgs e)
        => AddForm.Visibility = Visibility.Collapsed;

    private void SaveApp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LabelInput.Text) || string.IsNullOrWhiteSpace(UrlInput.Text)) return;

        var newItem = new QuickLaunchItem
        {
            Label    = LabelInput.Text.Trim(),
            Url      = UrlInput.Text.Trim(),
            Emoji    = string.IsNullOrWhiteSpace(EmojiInput.Text) ? "🔗" : EmojiInput.Text.Trim(),
            Category = string.IsNullOrWhiteSpace(CategoryInput.Text) ? "Tools" : CategoryInput.Text.Trim()
        };
        _apps.Add(newItem);

        // BUG 2: persist custom app so it survives restarts
        var s = SettingsService.Load();
        s.CustomQuickLaunchItems.Add(newItem);
        SettingsService.Save(s);

        LabelInput.Text = UrlInput.Text = "";
        AddForm.Visibility = Visibility.Collapsed;
        BuildCategoryFilters();
        RenderTiles();
    }
}
