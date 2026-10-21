namespace StreamCommand.Services;

/// <summary>A rectangle as 0–1 fractions of the frame, so it survives OBS rescaling the recording.</summary>
public sealed record NormalizedRect(double X, double Y, double W, double H);

/// <summary>Something worth clipping, positioned by how far into the recording it happened.</summary>
public sealed record ClipMoment(TimeSpan At, string Reason, double Score, bool Manual = false);

/// <summary>A planned clip: a window of the recording plus why it was picked.</summary>
public sealed record ClipSegment(TimeSpan Start, TimeSpan Duration, string Reason, double Score);

public enum ClipLayout { BlurFill, FacecamSplit }

public static class FacecamMath
{
    // OBS alignment bits: 1 = left, 2 = right, 4 = top, 8 = bottom; neither = centred on that axis.
    private const int AlignLeft = 1, AlignRight = 2, AlignTop = 4, AlignBottom = 8;

    /// <summary>
    /// Converts an OBS scene-item transform into a canvas fraction. Position is the item's
    /// alignment anchor, not its top-left, and a bounds box (when set) replaces the scaled size.
    /// Returns null for an item that is off-canvas or too small to be a real facecam.
    /// </summary>
    public static NormalizedRect? ToNormalized(double posX, double posY, double width, double height,
        string boundsType, double boundsW, double boundsH, int alignment, double canvasW, double canvasH)
    {
        if (boundsType != "OBS_BOUNDS_NONE" && boundsW > 0 && boundsH > 0) (width, height) = (boundsW, boundsH);
        if (width <= 0 || height <= 0 || canvasW <= 0 || canvasH <= 0) return null;

        double x = (alignment & AlignLeft) != 0 ? posX : (alignment & AlignRight) != 0 ? posX - width : posX - width / 2;
        double y = (alignment & AlignTop) != 0 ? posY : (alignment & AlignBottom) != 0 ? posY - height : posY - height / 2;

        double x0 = Math.Clamp(x / canvasW, 0, 1), y0 = Math.Clamp(y / canvasH, 0, 1);
        double x1 = Math.Clamp((x + width) / canvasW, 0, 1), y1 = Math.Clamp((y + height) / canvasH, 0, 1);
        if (x1 - x0 < 0.05 || y1 - y0 < 0.05) return null;
        return new NormalizedRect(x0, y0, x1 - x0, y1 - y0);
    }
}

public static class ClipPlanner
{
    /// <summary>
    /// Turns raw moments into the clips to render: each moment becomes [At − pre, At + post],
    /// overlapping windows merge (one hype burst often trips chat AND audio), then the best
    /// <paramref name="maxClips"/> survive in timeline order. Manual clips always survive —
    /// the streamer pressing "clip that" outranks any heuristic.
    /// </summary>
    public static IReadOnlyList<ClipSegment> Plan(IEnumerable<ClipMoment> moments, TimeSpan pre, TimeSpan post,
        TimeSpan recordingLength, int maxClips)
    {
        var windows = moments
            .Select(m => (Start: Max(TimeSpan.Zero, m.At - pre), End: Min(recordingLength, m.At + post),
                          m.Reason, Score: m.Manual ? double.MaxValue : m.Score, m.Manual))
            .Where(w => w.End - w.Start >= TimeSpan.FromSeconds(3))
            .OrderBy(w => w.Start)
            .ToList();

        var merged = new List<(TimeSpan Start, TimeSpan End, List<string> Reasons, double Score, bool Manual)>();
        foreach (var w in windows)
        {
            if (merged.Count > 0 && w.Start <= merged[^1].End)
            {
                var last = merged[^1];
                if (!last.Reasons.Contains(w.Reason)) last.Reasons.Add(w.Reason);
                merged[^1] = (last.Start, Max(last.End, w.End), last.Reasons, Math.Max(last.Score, w.Score), last.Manual || w.Manual);
            }
            else merged.Add((w.Start, w.End, [w.Reason], w.Score, w.Manual));
        }

        // Merging can chain a long hype streak into one window; cap so it still fits a short.
        var maxLen = TimeSpan.FromSeconds(60);
        return merged
            .OrderByDescending(m => m.Manual).ThenByDescending(m => m.Score)
            .Take(Math.Max(maxClips, merged.Count(m => m.Manual)))
            .OrderBy(m => m.Start)
            .Select(m => new ClipSegment(m.Start, Min(m.End - m.Start, maxLen), string.Join(" + ", m.Reasons),
                                         m.Manual ? 100 : m.Score))
            .ToList();
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}

/// <summary>
/// Spots chat going off: a burst well above this stream's own normal pace, a pile of hype
/// emotes, or a mod/VIP/the streamer typing !clip. Relative to the channel's baseline so a busy
/// chat doesn't clip constantly and a small chat can still trigger.
/// </summary>
public sealed class ChatHypeDetector(TimeSpan cooldown)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private const int MinBurst = 6;

    // Lowercased tokens; matched as whole words so "w" doesn't fire on every message containing one.
    private static readonly HashSet<string> HypeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "lul", "lol", "lmao", "lmfao", "kekw", "omegalul", "pog", "poggers", "pogchamp", "pogu", "w", "ww", "www",
        "clip", "clipit", "omg", "wtf", "insane", "holy", "noway", "monkas", "pepelaugh", "icant", "letsgo", "goated",
    };

    private readonly Queue<(DateTime At, bool Hype)> _recent = new();
    private double _baselinePerWindow;   // EMA of messages per window
    private DateTime _lastBaselineTick = DateTime.MinValue;
    private DateTime _lastTrigger = DateTime.MinValue;

    /// <summary>Feeds one chat message; returns a moment reason when this message tips chat into a highlight.</summary>
    public string? Add(DateTime now, string text, bool canCommandClip)
    {
        if (canCommandClip && text.TrimStart().StartsWith("!clip", StringComparison.OrdinalIgnoreCase))
            return Trigger(now, "!clip in chat", ignoreCooldown: true);

        _recent.Enqueue((now, IsHype(text)));
        while (_recent.Count > 0 && now - _recent.Peek().At > Window) _recent.Dequeue();
        UpdateBaseline(now);

        int count = _recent.Count, hype = _recent.Count(r => r.Hype);
        if (hype >= 4 && hype * 2 >= count) return Trigger(now, "chat hype");
        // "Spike" means nothing until we know this chat's normal pace — without the warm-up a
        // merely busy chat clipped itself every cooldown for the first minutes of each stream.
        if (_baselineTicks >= WarmupTicks && count >= Math.Max(MinBurst, _baselinePerWindow * 3)) return Trigger(now, "chat spike");
        return null;
    }

    private const int WarmupTicks = 6;   // one minute of 10s samples
    private int _baselineTicks;

    private void UpdateBaseline(DateTime now)
    {
        // Sample the windowed count every 10s: a plain average while warming up, then a slow
        // EMA (~5 minutes) so a sustained busy chat becomes the new normal instead of one endless spike.
        if (_lastBaselineTick == DateTime.MinValue) { _lastBaselineTick = now; return; }
        while (now - _lastBaselineTick >= Window)
        {
            _lastBaselineTick += Window;
            _baselineTicks++;
            _baselinePerWindow = _baselineTicks <= WarmupTicks
                ? _baselinePerWindow + (_recent.Count - _baselinePerWindow) / _baselineTicks
                : _baselinePerWindow * 0.97 + _recent.Count * 0.03;
        }
    }

    private string? Trigger(DateTime now, string reason, bool ignoreCooldown = false)
    {
        if (!ignoreCooldown && now - _lastTrigger < cooldown) return null;
        _lastTrigger = now;
        _recent.Clear();   // the burst is spent; don't re-trigger on its tail
        return reason;
    }

    public static bool IsHype(string text)
    {
        foreach (var word in text.Split([' ', '!', '?', '.', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (HypeTokens.Contains(word) || word.Length >= 4 && word.All(c => c is 'W' or 'w')) return true;
        return text.Contains("no way", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Spots the streamer getting loud (yelling, laughing) from the OBS mic meter. Needs the level
/// held high across most of a short window — one plosive or desk bump peaks just as hard.
/// </summary>
public sealed class AudioPeakDetector(TimeSpan cooldown, double threshold = 0.6)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);
    private readonly Queue<(DateTime At, bool Loud)> _samples = new();
    private DateTime _lastTrigger = DateTime.MinValue;

    public bool Add(DateTime now, double peak)
    {
        _samples.Enqueue((now, peak >= threshold));
        while (_samples.Count > 0 && now - _samples.Peek().At > Window) _samples.Dequeue();

        // ~20 samples/s from OBS; demand a near-full window so a burst at startup can't trigger.
        if (_samples.Count < 20 || now - _lastTrigger < cooldown) return false;
        if (_samples.Count(s => s.Loud) * 10 < _samples.Count * 6) return false;

        _lastTrigger = now;
        _samples.Clear();
        return true;
    }
}
