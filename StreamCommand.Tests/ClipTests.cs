using StreamCommand.Services;
using Xunit;

namespace StreamCommand.Tests;

public class ClipTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 20, 0, 0);
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    // ── Planner ──────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_MomentBecomesPrePostWindow()
    {
        var seg = Assert.Single(ClipPlanner.Plan([new(S(100), "chat spike", 60)], S(25), S(5), S(600), 8));
        Assert.Equal(S(75), seg.Start);
        Assert.Equal(S(30), seg.Duration);
    }

    [Fact]
    public void Plan_ClampsToRecordingEdges()
    {
        // A "clip that" 10s into the recording can't reach back 25s; one at the very end can't run past it.
        var segs = ClipPlanner.Plan([new(S(10), "a", 50), new(S(598), "b", 50)], S(25), S(5), S(600), 8);
        Assert.Equal(TimeSpan.Zero, segs[0].Start);
        Assert.Equal(S(15), segs[0].Duration);
        Assert.Equal(S(600), segs[1].Start + segs[1].Duration);
    }

    [Fact]
    public void Plan_OverlappingMomentsMergeIntoOneClip()
    {
        // Chat and the mic usually fire on the same hype — that's one clip, not two near-duplicates.
        var seg = Assert.Single(ClipPlanner.Plan([new(S(100), "chat spike", 60), new(S(104), "loud moment", 50)], S(25), S(5), S(600), 8));
        Assert.Equal(S(75), seg.Start);
        Assert.Equal(S(34), seg.Duration);
        Assert.Equal("chat spike + loud moment", seg.Reason);
    }

    [Fact]
    public void Plan_KeepsBestScoresInTimelineOrder()
    {
        var moments = Enumerable.Range(1, 6).Select(i => new ClipMoment(S(i * 100), $"m{i}", i)).ToList();
        var segs = ClipPlanner.Plan(moments, S(25), S(5), S(1000), 3);
        Assert.Equal(["m4", "m5", "m6"], segs.Select(s => s.Reason));
    }

    [Fact]
    public void Plan_ManualClipsAlwaysSurviveTheCap()
    {
        var moments = new List<ClipMoment>
        {
            new(S(100), "auto1", 90), new(S(200), "auto2", 80),
            new(S(300), "you clipped it", 1, Manual: true),
        };
        var segs = ClipPlanner.Plan(moments, S(25), S(5), S(1000), 1);
        Assert.Contains(segs, s => s.Reason == "you clipped it");
    }

    [Fact]
    public void Plan_LongMergedStreakIsCappedForShorts()
    {
        var moments = Enumerable.Range(0, 10).Select(i => new ClipMoment(S(100 + i * 20), "chat hype", 60)).ToList();
        var seg = Assert.Single(ClipPlanner.Plan(moments, S(25), S(5), S(1000), 8));
        Assert.Equal(S(60), seg.Duration);
    }

    // ── Chat detector ────────────────────────────────────────────────────────

    [Fact]
    public void Chat_TrustedClipCommandFiresImmediately()
        => Assert.Equal("!clip in chat", new ChatHypeDetector(S(45)).Add(T0, "!clip that was insane", canCommandClip: true));

    [Fact]
    public void Chat_UntrustedClipCommandIsJustAMessage()
        => Assert.Null(new ChatHypeDetector(S(45)).Add(T0, "!clip", canCommandClip: false));

    [Fact]
    public void Chat_HypeEmoteBurstFires()
    {
        var d = new ChatHypeDetector(S(45));
        string? hit = null;
        foreach (var (msg, i) in new[] { "KEKW", "LUL", "no way", "OMEGALUL" }.Select((m, i) => (m, i)))
            hit ??= d.Add(T0.AddSeconds(i), msg, false);
        Assert.Equal("chat hype", hit);
    }

    [Fact]
    public void Chat_SpikeAboveBaselineFires_ThenCoolsDown()
    {
        var d = new ChatHypeDetector(S(45));
        for (int i = 0; i < 20; i++) Assert.Null(d.Add(T0.AddSeconds(i * 5), "quiet chat", false));   // ~2 per 10s

        var burst = T0.AddSeconds(100);
        string? first = null;
        for (int i = 0; i < 12 && first is null; i++) first = d.Add(burst.AddSeconds(i * 0.5), $"msg {i}", false);
        Assert.Equal("chat spike", first);

        // Same burst continuing inside the cooldown must not mint a second clip.
        string? again = null;
        for (int i = 0; i < 10; i++) again ??= d.Add(burst.AddSeconds(10 + i * 0.5), $"more {i}", false);
        Assert.Null(again);
    }

    [Fact]
    public void Chat_NoSpikeBeforeTheBaselineIsLearned()
    {
        // The first minute of a stream is everyone saying hi — busy, but not a highlight.
        var d = new ChatHypeDetector(S(45));
        for (int i = 0; i < 40; i++) Assert.Null(d.Add(T0.AddSeconds(i), $"hi {i}", false));
    }

    [Fact]
    public void Chat_SteadyBusyChatBecomesTheBaseline()
    {
        // A channel that always does ~8 messages per 10s shouldn't clip every 45s forever.
        var d = new ChatHypeDetector(S(45));
        int fires = 0;
        for (int i = 0; i < 2400; i++)   // 1.25s apart for 50 minutes
            if (d.Add(T0.AddSeconds(i * 1.25), "just chatting", false) is not null) fires++;
        Assert.True(fires <= 3, $"fired {fires} times on steady chat");
    }

    [Theory]
    [InlineData("WWWW", true)]
    [InlineData("that was a W", true)]
    [InlineData("what a week", false)]
    [InlineData("hello world", false)]
    public void Chat_HypeWordsMatchWholeWords(string text, bool hype)
        => Assert.Equal(hype, ChatHypeDetector.IsHype(text));

    // ── Audio detector ───────────────────────────────────────────────────────

    [Fact]
    public void Audio_SustainedYellFires()
    {
        var d = new AudioPeakDetector(S(45));
        bool hit = false;
        for (int i = 0; i < 40; i++) hit |= d.Add(T0.AddMilliseconds(i * 50), 0.85);
        Assert.True(hit);
    }

    [Fact]
    public void Audio_SinglePeakDoesNot()
    {
        // A plosive or desk bump peaks just as hard as a yell — but only for a sample or two.
        var d = new AudioPeakDetector(S(45));
        bool hit = false;
        for (int i = 0; i < 60; i++) hit |= d.Add(T0.AddMilliseconds(i * 50), i % 15 == 0 ? 1.0 : 0.2);
        Assert.False(hit);
    }

    // ── Facecam rect ─────────────────────────────────────────────────────────

    [Fact]
    public void Facecam_TopLeftAlignedItem()
    {
        var r = FacecamMath.ToNormalized(1440, 0, 480, 270, "OBS_BOUNDS_NONE", 0, 0, alignment: 5, 1920, 1080)!;
        Assert.Equal(0.75, r.X, 3);
        Assert.Equal(0, r.Y, 3);
        Assert.Equal(0.25, r.W, 3);
        Assert.Equal(0.25, r.H, 3);
    }

    [Fact]
    public void Facecam_CentreAlignedItemUsesPositionAsMiddle()
    {
        var r = FacecamMath.ToNormalized(960, 540, 480, 270, "OBS_BOUNDS_NONE", 0, 0, alignment: 0, 1920, 1080)!;
        Assert.Equal(0.375, r.X, 3);
        Assert.Equal(0.375, r.Y, 3);
    }

    [Fact]
    public void Facecam_BottomRightAlignment()
    {
        var r = FacecamMath.ToNormalized(1920, 1080, 480, 270, "OBS_BOUNDS_NONE", 0, 0, alignment: 10, 1920, 1080)!;
        Assert.Equal(0.75, r.X, 3);
        Assert.Equal(0.75, r.Y, 3);
    }

    [Fact]
    public void Facecam_BoundsBoxReplacesScaledSize()
    {
        var r = FacecamMath.ToNormalized(0, 0, 9999, 9999, "OBS_BOUNDS_SCALE_INNER", 384, 216, 5, 1920, 1080)!;
        Assert.Equal(0.2, r.W, 3);
        Assert.Equal(0.2, r.H, 3);
    }

    [Fact]
    public void Facecam_OffCanvasOrTinyIsIgnored()
    {
        Assert.Null(FacecamMath.ToNormalized(3000, 0, 480, 270, "OBS_BOUNDS_NONE", 0, 0, 5, 1920, 1080));
        Assert.Null(FacecamMath.ToNormalized(0, 0, 20, 20, "OBS_BOUNDS_NONE", 0, 0, 5, 1920, 1080));
    }

    // ── Render args ──────────────────────────────────────────────────────────

    [Fact]
    public void Filter_SplitStacksCamOverGameAt1080x1920()
    {
        var f = ClipRenderer.BuildFilter(ClipLayout.FacecamSplit, new(0.75, 0, 0.25, 0.25));
        Assert.Contains("crop=trunc(iw*0.25/2)*2:trunc(ih*0.25/2)*2:iw*0.75:ih*0", f);
        Assert.Contains("vstack", f);
        Assert.Contains("scale=1080:640", f);
        Assert.Contains("scale=1080:1280", f);
    }

    [Fact]
    public void Filter_SplitWithoutFacecamFallsBackToBlur()
        => Assert.Contains("boxblur", ClipRenderer.BuildFilter(ClipLayout.FacecamSplit, null));

    [Fact]
    public void Filter_UsesInvariantDecimals()
    {
        // A German/French PC would otherwise write "0,75" and break the ffmpeg expression.
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.DoesNotContain("0,75", ClipRenderer.BuildFilter(ClipLayout.FacecamSplit, new(0.75, 0, 0.25, 0.25)));
            Assert.Contains("-ss 75.5 ", ClipRenderer.BuildArgs("in.mp4", "out.mp4", new(S(75.5), S(30), "x", 1), "f", nvenc: true));
        }
        finally { Thread.CurrentThread.CurrentCulture = prev; }
    }

    [Fact]
    public void Args_CpuFallbackUsesX264()
        => Assert.Contains("libx264", ClipRenderer.BuildArgs("in.mp4", "out.mp4", new(S(0), S(30), "x", 1), "f", nvenc: false));

    [Fact]
    public void Hotkey_ClipThatIsF9()
        => Assert.Equal(0x78u, HotkeyBindings.For(HotkeyModifierSet.CtrlShift).Single(b => b.Action == HotkeyAction.ClipThat).VirtualKey);

    [Fact]
    public void NewInstalls_ClipWithStreamButDontHoardRecordings()
    {
        var s = new AppSettings();
        Assert.True(s.ClipWithStream);
        Assert.True(s.AutoClipEnabled);
        Assert.False(s.SaveFullStream);
    }
}
