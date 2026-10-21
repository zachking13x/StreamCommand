using System.Windows;
using System.Windows.Controls;

namespace StreamCommand.Views;

public partial class HudPageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(HudPageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(HudPageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(HudPageHeader), new PropertyMetadata(null));

    public HudPageHeader() => InitializeComponent();

    public string Title    { get => (string)GetValue(TitleProperty);    set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    /// <summary>Right-aligned header content, e.g. a "+ New Rule" button.</summary>
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
}
