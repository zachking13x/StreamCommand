namespace StreamCommand.Services;

public enum HotkeyAction { ToggleStream, ToggleMic, SwitchScene, ClipThat }

/// <summary>The modifier every shortcut shares. Selectable because any fixed chord is already
/// owned by some other app on some PCs (on the dev box: Ctrl+Shift+F8 and Ctrl+Shift+F12).</summary>
public enum HotkeyModifierSet { CtrlShift, CtrlAlt, CtrlShiftAlt }

/// <summary>One system-wide shortcut. <see cref="SceneIndex"/> is 0-based and only used by SwitchScene.</summary>
public sealed record HotkeyBinding(int Id, HotkeyAction Action, uint Modifiers, uint VirtualKey, string Label, int SceneIndex = -1);

/// <summary>
/// The shortcut table: modifier + F-key only. Never letters/digits — Ctrl+Shift+M is Discord's
/// default mute (both would fire) and Ctrl+Alt+digit is AltGr on European layouts (it would eat
/// "{", "[" …). Never bare F-keys — most games bind them. F12 is avoided: Windows reserves it for
/// debuggers and it's the most commonly taken.
/// </summary>
public static class HotkeyBindings
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    private const uint VkF1 = 0x70;

    public const int MaxScenes = 8;
    public const int MicKey    = 10;   // F10
    public const int StreamKey = 11;   // F11
    public const int ClipKey   = 9;    // F9: the one F-key left between the scene slots and the mic

    public static uint ModifiersFor(HotkeyModifierSet set) => set switch
    {
        HotkeyModifierSet.CtrlAlt      => ModControl | ModAlt,
        HotkeyModifierSet.CtrlShiftAlt => ModControl | ModShift | ModAlt,
        _                              => ModControl | ModShift,
    };

    public static string PrefixFor(HotkeyModifierSet set) => set switch
    {
        HotkeyModifierSet.CtrlAlt      => "Ctrl + Alt",
        HotkeyModifierSet.CtrlShiftAlt => "Ctrl + Shift + Alt",
        _                              => "Ctrl + Shift",
    };

    public static HotkeyModifierSet Parse(string? value)
        => Enum.TryParse<HotkeyModifierSet>(value, out var s) ? s : HotkeyModifierSet.CtrlShift;

    public static IReadOnlyList<HotkeyBinding> For(HotkeyModifierSet set)
    {
        var mods   = ModifiersFor(set) | ModNoRepeat;   // NoRepeat: holding the key fires once
        var prefix = PrefixFor(set);
        var list = new List<HotkeyBinding>();
        for (int i = 0; i < MaxScenes; i++)
            list.Add(new(100 + i, HotkeyAction.SwitchScene, mods, VkF1 + (uint)i, $"{prefix} + F{i + 1}", i));
        list.Add(new(200, HotkeyAction.ToggleMic,    mods, VkF1 + MicKey - 1,    $"{prefix} + F{MicKey}"));
        list.Add(new(201, HotkeyAction.ToggleStream, mods, VkF1 + StreamKey - 1, $"{prefix} + F{StreamKey}"));
        list.Add(new(202, HotkeyAction.ClipThat,     mods, VkF1 + ClipKey - 1,   $"{prefix} + F{ClipKey}"));
        return list;
    }

    /// <summary>Short chip text for a scene slot, e.g. "F3"; empty past the last bound slot.</summary>
    public static string SceneKey(int sceneIndex) => sceneIndex is >= 0 and < MaxScenes ? $"F{sceneIndex + 1}" : "";
}

/// <summary>
/// Two-press confirmation for actions you can't take back mid-game (going live / ending the
/// stream). The first press arms it; a second press inside the window confirms. A lone press
/// just times out — no dialog, so focus never leaves the game.
/// </summary>
public sealed class DoublePressGuard(TimeSpan window)
{
    private DateTime? _armedAt;

    public TimeSpan Window { get; } = window;

    /// <summary>True when this press confirms; false when it only arms (or re-arms after expiry).</summary>
    public bool Press(DateTime now)
    {
        if (_armedAt is DateTime t && now - t <= Window)
        {
            _armedAt = null;
            return true;
        }
        _armedAt = now;
        return false;
    }

    public void Reset() => _armedAt = null;
}
