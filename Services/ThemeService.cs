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
    /// <summary>Hex of the currently active accent (e.g. "#6B9E85").</summary>
    public static string CurrentAccent { get; private set; } = "#6B9E85";

    private record Palette(
        string AccentBrush,
        string AccentLight,
        string AccentDark,
        string AccentMuted,
        string AccentBorder,
        string NavSelectedBg);

    // All keys uppercase so lookup is case-insensitive after normalisation.
    private static readonly Dictionary<string, Palette> _palettes = new()
    {
        ["#6B9E85"] = new("#6B9E85", "#8ABBA6", "#4A8970", "#1A2A22", "#2D5A45", "#1E3029"),  // Sage
        ["#2563EB"] = new("#2563EB", "#60A5FA", "#1D4ED8", "#0F1B35", "#1E3A8A", "#111C3A"),  // Blue
        ["#DB2777"] = new("#DB2777", "#F472B6", "#BE185D", "#2D0A1E", "#9D174D", "#1F0514"),  // Pink
        ["#16A34A"] = new("#16A34A", "#4ADE80", "#15803D", "#0A2215", "#166534", "#0D2718"),  // Green
        ["#EA580C"] = new("#EA580C", "#FB923C", "#C2410C", "#2D1205", "#9A3412", "#1F0A03"),  // Orange
    };

    /// <summary>
    /// Applies the palette for <paramref name="accentHex"/> to
    /// <see cref="Application.Current.Resources"/> and <see cref="ThemeColors"/>.
    /// No-ops if the hex is unrecognised (keeps current palette).
    /// </summary>
    public static void Apply(string? accentHex)
    {
        if (string.IsNullOrWhiteSpace(accentHex)) return;

        var key = accentHex.ToUpperInvariant();
        if (!_palettes.TryGetValue(key, out var p)) return;

        CurrentAccent = key;

        var res = Application.Current.Resources;
        res["AccentBrush"]     = MakeBrush(p.AccentBrush);
        res["AccentLight"]     = MakeBrush(p.AccentLight);
        res["AccentDark"]      = MakeBrush(p.AccentDark);
        res["AccentMuted"]     = MakeBrush(p.AccentMuted);
        res["AccentBorder"]    = MakeBrush(p.AccentBorder);
        res["NavSelectedBg"]   = MakeBrush(p.NavSelectedBg);
        res["ButtonPrimaryBg"] = MakeBrush(p.AccentBrush);

        // Keep ThemeColors in sync for code-behind chart drawing
        ThemeColors.Accent      = ColorFromHex(p.AccentBrush);
        ThemeColors.AccentLight = ColorFromHex(p.AccentLight);
        ThemeColors.AccentMuted = ColorFromHex(p.AccentMuted);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SolidColorBrush MakeBrush(string hex)
    {
        var b = new SolidColorBrush(ColorFromHex(hex));
        b.Freeze();   // cross-thread safe + tiny perf win
        return b;
    }

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex[0..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return Color.FromRgb(r, g, b);
    }
}
