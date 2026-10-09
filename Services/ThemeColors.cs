using System.Windows.Media;

namespace StreamCommand.Services;

/// <summary>
/// Single source of truth for colours used in code-behind (chart drawing etc.).
/// Keep in sync with DarkTheme.xaml — never hardcode hex values in C# directly.
/// Navy/teal palette: base #0D1117, accent #00C9A7.
/// </summary>
public static class ThemeColors
{
    // Accent — mutable so ThemeService can update them at runtime
    public static Color Accent      = Color.FromRgb(0x00, 0xC9, 0xA7); // #00C9A7 teal
    public static Color AccentLight = Color.FromRgb(0x33, 0xD6, 0xB9); // #33D6B9 teal light
    public static Color AccentMuted = Color.FromRgb(0x0A, 0x2A, 0x20); // #0a2a20

    // Status
    public static readonly Color Success     = Color.FromRgb(0x22, 0xC5, 0x5E);
    public static readonly Color Danger      = Color.FromRgb(0xEF, 0x44, 0x44);
    public static readonly Color Warning     = Color.FromRgb(0xF5, 0x9E, 0x0B);

    // Text
    public static readonly Color PrimaryText = Color.FromRgb(0xE8, 0xED, 0xF2); // #e8edf2
    public static readonly Color MutedText   = Color.FromRgb(0x3A, 0x50, 0x68); // #3a5068

    // Surfaces
    public static readonly Color CardBg      = Color.FromRgb(0x11, 0x1C, 0x2A); // #111c2a
    public static readonly Color AppBg       = Color.FromRgb(0x0D, 0x11, 0x17); // #0D1117
    public static readonly Color SidebarBg   = Color.FromRgb(0x0A, 0x0E, 0x14); // #0a0e14

    // Borders
    public static readonly Color Border      = Color.FromRgb(0x1E, 0x27, 0x33); // #1e2733
    public static readonly Color BorderStrong= Color.FromRgb(0x2A, 0x3A, 0x4A); // #2a3a4a
}
