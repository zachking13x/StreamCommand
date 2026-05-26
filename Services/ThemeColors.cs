using System.Windows.Media;

namespace StreamCommand.Services;

/// <summary>
/// Single source of truth for colours used in code-behind (chart drawing etc.).
/// Keep in sync with DarkTheme.xaml — never hardcode hex values in C# directly.
/// </summary>
public static class ThemeColors
{
    // Accent — mutable so ThemeService can update them at runtime
    public static Color Accent      = Color.FromRgb(0x6B, 0x9E, 0x85);
    public static Color AccentLight = Color.FromRgb(0x8A, 0xBB, 0xA6);
    public static Color AccentMuted = Color.FromRgb(0x1A, 0x2A, 0x22);

    // Status
    public static readonly Color Success     = Color.FromRgb(0x22, 0xC5, 0x5E);
    public static readonly Color Danger      = Color.FromRgb(0xEF, 0x44, 0x44);
    public static readonly Color Warning     = Color.FromRgb(0xF5, 0x9E, 0x0B);

    // Text
    public static readonly Color PrimaryText = Color.FromRgb(0xD4, 0xDD, 0xD6);
    public static readonly Color MutedText   = Color.FromRgb(0x5E, 0x6E, 0x66);

    // Surfaces
    public static readonly Color CardBg      = Color.FromRgb(0x25, 0x2B, 0x2D);
    public static readonly Color AppBg       = Color.FromRgb(0x19, 0x1C, 0x1E);
}
