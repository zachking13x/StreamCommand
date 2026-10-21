using StreamCommand.Services;
using Xunit;

namespace StreamCommand.Tests;

public class HotkeyTests
{
    private static readonly DateTime T0 = new(2026, 10, 12, 12, 0, 0);

    [Fact]
    public void GoLive_FirstPressOnlyArms()
        => Assert.False(new DoublePressGuard(TimeSpan.FromSeconds(3)).Press(T0));

    [Fact]
    public void GoLive_SecondPressInsideWindowConfirms()
    {
        var g = new DoublePressGuard(TimeSpan.FromSeconds(3));
        g.Press(T0);
        Assert.True(g.Press(T0.AddSeconds(2)));
    }

    [Fact]
    public void GoLive_SlowSecondPressReArmsInsteadOfConfirming()
    {
        // A stray press, then another much later, must never start a broadcast.
        var g = new DoublePressGuard(TimeSpan.FromSeconds(3));
        g.Press(T0);
        Assert.False(g.Press(T0.AddSeconds(5)));
        Assert.True(g.Press(T0.AddSeconds(6)));
    }

    [Fact]
    public void GoLive_ConfirmDisarms_SoAThirdPressStartsOver()
    {
        var g = new DoublePressGuard(TimeSpan.FromSeconds(3));
        g.Press(T0);
        g.Press(T0.AddSeconds(1));
        Assert.False(g.Press(T0.AddSeconds(1.5)));
    }

    [Fact]
    public void GoLive_ResetCancelsAnArmedPress()
    {
        var g = new DoublePressGuard(TimeSpan.FromSeconds(3));
        g.Press(T0);
        g.Reset();
        Assert.False(g.Press(T0.AddSeconds(1)));
    }

    public static TheoryData<HotkeyModifierSet> Sets => new() { HotkeyModifierSet.CtrlShift, HotkeyModifierSet.CtrlAlt, HotkeyModifierSet.CtrlShiftAlt };

    [Theory, MemberData(nameof(Sets))]
    public void Bindings_AreUniqueIdsAndKeys(HotkeyModifierSet set)
    {
        var all = HotkeyBindings.For(set);
        Assert.Equal(all.Count, all.Select(b => b.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(b => (b.Modifiers, b.VirtualKey)).Distinct().Count());
    }

    [Theory, MemberData(nameof(Sets))]
    public void Bindings_AlwaysHaveCtrl_AndOnlyFKeys(HotkeyModifierSet set)
    {
        // Never bare keys (games bind them), never letters/digits (AltGr / Discord clashes).
        foreach (var b in HotkeyBindings.For(set))
        {
            Assert.NotEqual(0u, b.Modifiers & HotkeyBindings.ModControl);
            Assert.InRange(b.VirtualKey, 0x70u, 0x7Au);   // F1–F11
            Assert.StartsWith(HotkeyBindings.PrefixFor(set), b.Label);
        }
    }

    [Fact]
    public void Bindings_NeverUseF12()
        // Windows reserves F12 for debuggers and it was already taken on the dev PC.
        => Assert.DoesNotContain(HotkeyBindings.For(HotkeyModifierSet.CtrlShift), b => b.VirtualKey == 0x7B);

    [Fact]
    public void Bindings_SceneSlotsMatchTheirFKey()
    {
        var scenes = HotkeyBindings.For(HotkeyModifierSet.CtrlShift).Where(b => b.Action == HotkeyAction.SwitchScene).ToList();
        Assert.Equal(HotkeyBindings.MaxScenes, scenes.Count);
        for (int i = 0; i < scenes.Count; i++)
        {
            Assert.Equal(i, scenes[i].SceneIndex);
            Assert.Equal(0x70u + (uint)i, scenes[i].VirtualKey);           // VK_F1 + i
            Assert.Equal($"F{i + 1}", HotkeyBindings.SceneKey(i));
        }
        Assert.Equal("", HotkeyBindings.SceneKey(HotkeyBindings.MaxScenes));
    }

    [Fact]
    public void Bindings_StreamToggleIsF11_MicIsF10()
    {
        var all = HotkeyBindings.For(HotkeyModifierSet.CtrlShift);
        Assert.Equal(0x7Au, all.Single(b => b.Action == HotkeyAction.ToggleStream).VirtualKey);
        Assert.Equal(0x79u, all.Single(b => b.Action == HotkeyAction.ToggleMic).VirtualKey);
    }

    [Theory]
    [InlineData(null, HotkeyModifierSet.CtrlShift)]
    [InlineData("garbage", HotkeyModifierSet.CtrlShift)]
    [InlineData("CtrlAlt", HotkeyModifierSet.CtrlAlt)]
    public void Parse_FallsBackToCtrlShift(string? stored, HotkeyModifierSet expected)
        => Assert.Equal(expected, HotkeyBindings.Parse(stored));

    [Fact]
    public void NewInstalls_HaveShortcutsOn()
        => Assert.True(new AppSettings().GlobalHotkeysEnabled);
}
