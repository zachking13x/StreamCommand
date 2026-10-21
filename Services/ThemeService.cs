using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace StreamCommand.Services;

/// <summary>
/// Applies a runtime accent-colour palette to all DynamicResource keys.
/// Keeps ThemeColors static fields in sync so code-behind chart drawing
/// always uses the correct colour without re-reading the ResourceDictionary.
/// </summary>
public static class ThemeService
{
    /// <summary>Hex of the currently active accent (e.g. "#00C9A7").</summary>
    public static string CurrentAccent { get; private set; } = DefaultAccent;

    /// <summary>The brand teal — also what DarkTheme.xaml ships with.</summary>
    public const string DefaultAccent = "#00C9A7";

    // Sage was the default before the navy/teal rebrand, but the default setting was never
    // moved — so every install kept painting sage over the teal theme. Sage was never offered
    // as anything but "the default", so treat it as the brand slot it occupied.
    private const string LegacyDefaultAccent = "#6B9E85";

    private record Palette(
        string AccentBrush,
        string AccentLight,
        string AccentDark,
        string AccentMuted,
        string AccentBorder);

    // All keys uppercase so lookup is case-insensitive after normalisation.
    private static readonly Dictionary<string, Palette> _palettes = new()
    {
        ["#00C9A7"] = new("#00C9A7", "#4FE3C8", "#00B394", "#0F2A2A", "#16594D"),  // Teal (brand)
        ["#2563EB"] = new("#2563EB", "#60A5FA", "#1D4ED8", "#0F1B35", "#1E3A8A"),  // Blue
        ["#DB2777"] = new("#DB2777", "#F472B6", "#BE185D", "#2D0A1E", "#9D174D"),  // Pink
        ["#16A34A"] = new("#16A34A", "#4ADE80", "#15803D", "#0A2215", "#166534"),  // Green
        ["#EA580C"] = new("#EA580C", "#FB923C", "#C2410C", "#2D1205", "#9A3412"),  // Orange
    };

    /// <summary>Every accent the Settings swatches offer.</summary>
    public static IReadOnlyCollection<string> Accents => _palettes.Keys;

    /// <summary>Upper-cased palette key, with the pre-rebrand default mapped to teal.</summary>
    public static string Normalize(string accentHex)
    {
        var key = accentHex.Trim().ToUpperInvariant();
        return key == LegacyDefaultAccent ? DefaultAccent : key;
    }

    /// <summary>
    /// Text colour for anything drawn ON the accent (button labels, PRO badges): whichever of
    /// near-black or white contrasts more. Teal/green/orange are light enough for dark text;
    /// blue and pink are not — a fixed dark label on them fell under 4:1.
    /// </summary>
    public static Color OnAccentFor(Color accent)
    {
        var l = RelativeLuminance(accent);
        var dark = Color.FromRgb(0x0A, 0x0E, 0x14);
        var vsDark  = (l + 0.05) / (RelativeLuminance(dark) + 0.05);
        var vsWhite = 1.05 / (l + 0.05);
        return vsDark >= vsWhite ? dark : Colors.White;
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    public static double RelativeLuminance(Color c)
    {
        static double Lin(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>
    /// Applies the palette for <paramref name="accentHex"/> to
    /// <see cref="Application.Current.Resources"/> and <see cref="ThemeColors"/>.
    /// No-ops if the hex is unrecognised (keeps current palette).
    /// </summary>
    public static void Apply(string? accentHex)
    {
        if (string.IsNullOrWhiteSpace(accentHex)) return;

        var key = Normalize(accentHex);
        if (!_palettes.TryGetValue(key, out var p)) return;

        CurrentAccent = key;

        var accent = ColorFromHex(p.AccentBrush);
        var light  = ColorFromHex(p.AccentLight);
        var dark   = ColorFromHex(p.AccentDark);
        var muted  = ColorFromHex(p.AccentMuted);

        var res = Application.Current.Resources;
        res["AccentColor"]     = accent;
        res["AccentBrush"]     = Freeze(new SolidColorBrush(accent));
        res["AccentLight"]     = Freeze(new SolidColorBrush(light));
        res["AccentDark"]      = Freeze(new SolidColorBrush(dark));
        res["AccentMuted"]     = Freeze(new SolidColorBrush(muted));
        res["AccentBorder"]    = MakeBrush(p.AccentBorder);
        res["ButtonPrimaryBg"] = Freeze(new SolidColorBrush(accent));
        res["ButtonPrimaryFg"] = Freeze(new SolidColorBrush(OnAccentFor(accent)));

        // Everything the facelift derives from the accent — same recipes as DarkTheme.xaml,
        // so switching swatches restyles the gradients/glows too, not just flat fills.
        res["HeroAccentFill"]  = Freeze(Vertical(Mix(accent, Colors.White, 0.12), accent, dark));
        res["ProPaneGradient"] = Freeze(new LinearGradientBrush(
            new GradientStopCollection { new(muted, 0), new(Color.FromRgb(0x0F, 0x18, 0x24), 0.5) },
            new Point(0, 0), new Point(0.6, 1)));
        res["AccentHairline"]  = Freeze(new LinearGradientBrush(
            new GradientStopCollection { new(WithAlpha(accent, 0xB0), 0), new(WithAlpha(accent, 0x00), 0.7) },
            new Point(0, 0), new Point(1, 0)));
        res["AmbientGlow"]     = Freeze(new RadialGradientBrush(
            new GradientStopCollection { new(WithAlpha(accent, 0x22), 0), new(WithAlpha(accent, 0x00), 1) }));
        res["RingGradient"]    = Freeze(new LinearGradientBrush(light, accent, 90));

        // Keep ThemeColors in sync for code-behind chart drawing
        ThemeColors.Accent      = accent;
        ThemeColors.AccentLight = light;
        ThemeColors.AccentMuted = muted;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }   // cross-thread safe + tiny perf win

    private static SolidColorBrush MakeBrush(string hex) => Freeze(new SolidColorBrush(ColorFromHex(hex)));

    private static LinearGradientBrush Vertical(Color top, Color mid, Color bottom) => new(
        new GradientStopCollection { new(top, 0), new(mid, 0.55), new(bottom, 1) }, new Point(0, 0), new Point(0, 1));

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex[0..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return Color.FromRgb(r, g, b);
    }
}
