using StreamCommand.Services;
using Xunit;

namespace StreamCommand.Tests;

/// <summary>
/// Gating rules for the paid-app model (v1.0.29+). The expensive mistake here is shipping
/// a $7.99 app that still hides features from the person who just bought it — the
/// CleanAimTracker 1.0.103 lockout in a new costume. These lock that behaviour down.
///
/// EntitlementService.RefreshAsync itself needs a live WinRT StoreContext, so its
/// fail-open path can only be confirmed by running the packaged app; what is testable is
/// that every gate opens once entitlement is granted.
/// </summary>
public class EntitlementGateTests
{
    // FeatureGate reads static entitlement state, so each test sets it explicitly.
    private static void SetEntitled(bool value) => EntitlementService.IsPro = value;

    public static readonly TheoryData<string> AllGatedFeatures = new()
    {
        "live-preview",
        "stream-alerts",
        "automation-unlimited",
        "planner-unlimited",
        "analytics-history",
        "pre-stream-edit",
        "quick-launch-unlimited",
        "multi-platform",
        "priority-support",
    };

    [Theory]
    [MemberData(nameof(AllGatedFeatures))]
    public void EntitledUser_GetsEveryFeature(string feature)
    {
        SetEntitled(true);
        Assert.True(FeatureGate.Has(feature), $"a paying user must not be gated out of '{feature}'");
        Assert.False(FeatureGate.IsLocked(feature));
    }

    [Theory]
    [MemberData(nameof(AllGatedFeatures))]
    public void UnentitledUser_IsGated(string feature)
    {
        SetEntitled(false);
        Assert.False(FeatureGate.Has(feature));
        Assert.True(FeatureGate.IsLocked(feature));
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("settings")]
    [InlineData("whats-new")]
    [InlineData("tools-hub")]
    [InlineData("setup-guide")]
    public void NavigationStaysUsable_EvenWhenNotEntitled(string feature)
    {
        // An expired trial must show a purchase prompt, not a bricked window.
        SetEntitled(false);
        Assert.True(FeatureGate.Has(feature), $"'{feature}' must stay reachable so the user can buy");
    }

    [Fact]
    public void UnknownFeatureKey_OpensForEntitledUser()
    {
        // Under the paid model there is no tier to fall through to: owning the app
        // grants everything, including any key added later.
        SetEntitled(true);
        Assert.True(FeatureGate.Has("some-feature-added-next-year"));
    }

    [Fact]
    public void FeatureKeysAreCaseInsensitive()
    {
        SetEntitled(false);
        Assert.True(FeatureGate.Has("DASHBOARD"));
        Assert.True(FeatureGate.Has("Settings"));
    }

    [Fact]
    public void IsEntitled_MirrorsIsPro()
    {
        SetEntitled(true);
        Assert.True(EntitlementService.IsEntitled);
        SetEntitled(false);
        Assert.False(EntitlementService.IsEntitled);
    }

    [Fact]
    public void TrialDaysRemaining_IsNull_WhenNotOnTrial()
    {
        SetEntitled(true);   // owner, not trial
        Assert.Null(EntitlementService.TrialDaysRemaining);
    }

    [Fact]
    public void CacheMarkers_AreDistinctAndNonEmpty()
    {
        // MainWindow validates the cached marker against these before trusting it.
        Assert.False(string.IsNullOrWhiteSpace(EntitlementService.OwnedMarker));
        Assert.False(string.IsNullOrWhiteSpace(EntitlementService.TrialMarker));
        Assert.NotEqual(EntitlementService.OwnedMarker, EntitlementService.TrialMarker);
    }
}
