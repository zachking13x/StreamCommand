using StreamCommand.Services;
using Xunit;

namespace StreamCommand.Tests;

public class PricingTests
{
    [Theory]
    [InlineData("$6.99", 6.99)]
    [InlineData("US$49.99", 49.99)]
    [InlineData("6,99 €", 6.99)]
    [InlineData("1.234,56 €", 1234.56)]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("¥1,200", 1200)]
    [InlineData("CHF 6.9", 6.9)]
    [InlineData("₹ 599", 599)]
    public void TryParseAmount_ReadsStoreFormattedPrices(string formatted, double expected)
    {
        Assert.True(PricingMath.TryParseAmount(formatted, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Free")]
    [InlineData("—")]
    public void TryParseAmount_RejectsNonPrices(string? formatted)
        => Assert.False(PricingMath.TryParseAmount(formatted, out _));

    [Fact]
    public void DisplayPrice_IgnoresZeroRecurrenceOnOneTimeAddOn()
        // Live Store data: the Lifetime add-on reports FormattedRecurrencePrice "$0.00".
        => Assert.Equal("$89.99", PricingMath.DisplayPrice(isSubscription: false, "$89.99", "$0.00"));

    [Fact]
    public void DisplayPrice_UsesRecurrencePriceForSubscriptions()
        // A trial makes the first-period price $0 — the buyer pays the recurrence price.
        => Assert.Equal("$6.99", PricingMath.DisplayPrice(isSubscription: true, "$0.00", "$6.99"));

    [Fact]
    public void DisplayPrice_SubscriptionWithoutRecurrenceFallsBackToPrice()
        => Assert.Equal("$6.99", PricingMath.DisplayPrice(isSubscription: true, "$6.99", ""));

    [Fact]
    public void AppPrice_FallbackIsTheOneTimeListPrice()
        // Shown only when the Store is unreachable. If the Partner Center price ever
        // changes, this is the string that would go stale.
        => Assert.Equal("$7.99", StorePricingService.FallbackPrice);

    [Fact]
    public void AppPrice_OneTimePurchaseIgnoresRecurrencePrice()
        // The Store fills FormattedRecurrencePrice with "$0.00" for a one-time buy;
        // taking it would show the app as free.
        => Assert.Equal("$7.99", PricingMath.DisplayPrice(isSubscription: false, "$7.99", "$0.00"));

}

public class ProgressRingTests
{
    // Guard for the old bug: pixel lengths were passed to StrokeDashArray (which is in
    // stroke-thickness units), so the ring drew full at any progress past ~20%.
    [Fact]
    public void Dashes_AreInStrokeThicknessUnits()
    {
        var (filled, gap) = ProgressRingMath.Dashes(1.0, diameter: 112, thickness: 10);
        Assert.Equal(Math.PI * 102 / 10, filled, 3);
        Assert.True(gap < 0.01);
    }

    [Fact]
    public void Dashes_HalfwayFillsHalfTheCircumference()
    {
        var (filled, gap) = ProgressRingMath.Dashes(0.5, diameter: 112, thickness: 10);
        Assert.Equal(filled, gap, 2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Dashes_ClampOutOfRangeProgress(double fraction)
    {
        var full = Math.PI * 102 / 10;
        var (filled, _) = ProgressRingMath.Dashes(fraction, diameter: 112, thickness: 10);
        Assert.InRange(filled, 0, full + 1e-9);
    }
}

public class ThemeAccentTests
{
    [Fact]
    public void LegacySageDefault_MapsToBrandTeal()
        // Sage was the pre-rebrand default and every install still had it saved,
        // painting sage over the teal theme on every launch.
        => Assert.Equal(ThemeService.DefaultAccent, ThemeService.Normalize("#6b9e85"));

    [Fact]
    public void ChosenAccents_PassThroughUnchanged()
        => Assert.Equal("#2563EB", ThemeService.Normalize(" #2563eb "));

    [Fact]
    public void NewInstalls_DefaultToTeal()
        => Assert.Equal("#00C9A7", new AppSettings().AccentColor);
}

public class AccentContrastTests
{
    // Every swatch's button label must clear 4.5:1 against its own fill.
    [Theory]
    [InlineData("#00C9A7")]
    [InlineData("#2563EB")]
    [InlineData("#DB2777")]
    [InlineData("#16A34A")]
    [InlineData("#EA580C")]
    public void OnAccentText_PassesAA_ForEverySwatch(string hex)
    {
        var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        var fg = ThemeService.OnAccentFor(accent);
        double l1 = ThemeService.RelativeLuminance(accent), l2 = ThemeService.RelativeLuminance(fg);
        var ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        Assert.True(ratio >= 4.5, $"{hex}: {ratio:F2}:1");
    }

    [Fact]
    public void EverySwatchHasAPalette()
        => Assert.Equal(5, ThemeService.Accents.Count);
}
