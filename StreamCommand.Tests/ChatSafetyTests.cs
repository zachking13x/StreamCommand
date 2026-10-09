using StreamCommand.Services;
using Xunit;

namespace StreamCommand.Tests;

/// <summary>
/// The two guards that stop automation misbehaving in a live channel: the rate limiter
/// (going over Twitch's limit silently mutes the account for 30 minutes) and dedup (a
/// replayed event must not fire an automated reply twice).
/// </summary>
public class ChatSafetyTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RateLimiter_AllowsTheBurst_ThenThrottles()
    {
        var rl = new ChatRateLimiter(burst: 3, window: TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.Zero, rl.TryReserve(T0));
        Assert.Equal(TimeSpan.Zero, rl.TryReserve(T0.AddSeconds(1)));
        Assert.Equal(TimeSpan.Zero, rl.TryReserve(T0.AddSeconds(2)));

        // 4th inside the window: wait until the FIRST send ages out (30s after T0).
        Assert.Equal(TimeSpan.FromSeconds(27), rl.TryReserve(T0.AddSeconds(3)));
    }

    [Fact]
    public void RateLimiter_ThrottledCall_ClaimsNoSlot()
    {
        // The writer polls while throttled. If a refused call still claimed a slot, every
        // poll would push the window further out and chat would never resume.
        var rl = new ChatRateLimiter(burst: 1, window: TimeSpan.FromSeconds(30));
        rl.TryReserve(T0);
        for (int i = 1; i <= 50; i++) rl.TryReserve(T0.AddMilliseconds(250 * i));

        Assert.Equal(TimeSpan.Zero, rl.TryReserve(T0.AddSeconds(30)));
    }

    [Fact]
    public void RateLimiter_SlotFreesExactlyAtWindowEdge()
    {
        var rl = new ChatRateLimiter(burst: 1, window: TimeSpan.FromSeconds(30));
        rl.TryReserve(T0);
        Assert.True(rl.TryReserve(T0.AddSeconds(29.9)) > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, rl.TryReserve(T0.AddSeconds(30)));
    }

    [Fact]
    public void RateLimiter_DefaultStaysUnderTwitchsTwentyPerThirty()
    {
        var rl = new ChatRateLimiter();
        int allowed = 0;
        for (int i = 0; i < 40; i++)
            if (rl.TryReserve(T0.AddMilliseconds(i * 100)) == TimeSpan.Zero) allowed++;
        Assert.True(allowed < 20, $"allowed {allowed} in 4s");
    }

    [Fact]
    public void Dedup_SecondSightingOfAnId_IsDuplicate()
    {
        var d = new MessageDeduplicator();
        Assert.False(d.IsDuplicate("evt-1", T0));
        Assert.True(d.IsDuplicate("evt-1", T0.AddSeconds(5)));
        Assert.False(d.IsDuplicate("evt-2", T0.AddSeconds(5)));
    }

    [Fact]
    public void Dedup_MissingId_AlwaysPassesThrough()
    {
        var d = new MessageDeduplicator();
        Assert.False(d.IsDuplicate("", T0));
        Assert.False(d.IsDuplicate("", T0));
    }

    [Fact]
    public void Dedup_ForgetsIdsOutsideTheWindow()
    {
        var d = new MessageDeduplicator(window: TimeSpan.FromMinutes(5));
        d.IsDuplicate("evt-1", T0);
        Assert.False(d.IsDuplicate("evt-1", T0.AddMinutes(6)));
    }

    [Fact]
    public void Dedup_StaysBounded_UnderFlood()
    {
        var d = new MessageDeduplicator(window: TimeSpan.FromHours(1), maxIds: 10);
        for (int i = 0; i < 100; i++) d.IsDuplicate($"id-{i}", T0);

        Assert.True(d.IsDuplicate("id-99", T0));    // recent ids still caught
        Assert.False(d.IsDuplicate("id-0", T0));    // oldest evicted, memory capped
    }
}
