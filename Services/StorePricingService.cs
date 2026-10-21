using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace StreamCommand.Services;

/// <summary>
/// Reads the app's live, localized price from the Microsoft Store so the purchase screen can
/// never drift from Partner Center. The price used to be hardcoded USD, which was wrong for
/// every non-US buyer.
///
/// One price now: the app is a paid one-time purchase with a 7-day Store trial, so there are
/// no plans to compare and no add-ons to look up.
/// </summary>
public static class StorePricingService
{
    /// <summary>
    /// US list price, shown only when the Store can't be reached (offline, or a dev build
    /// with no Store identity). The Store's own purchase dialog always shows the real
    /// localized price, so this is display-only.
    /// </summary>
    public const string FallbackPrice = "$7.99";

    private static string? _cached;

    /// <summary>Formatted price for the full app licence, e.g. "$7.99" or "7,99 EUR".</summary>
    public static async Task<string> GetAppPriceAsync()
    {
        if (_cached != null) return _cached;

        try
        {
            var ctx    = StoreContext.GetDefault();
            var result = await ctx.GetStoreProductForCurrentAppAsync();
            var price  = result?.Product?.Price;

            if (result?.ExtendedError != null || price == null)
                return FallbackPrice;

            // The app licence is a one-time purchase, so the recurrence price is meaningless
            // here (it comes back as "$0.00") - DisplayPrice already discards it.
            var display = PricingMath.DisplayPrice(isSubscription: false,
                                                   price.FormattedPrice,
                                                   price.FormattedRecurrencePrice);

            if (string.IsNullOrWhiteSpace(display)) return FallbackPrice;

            _cached = display;
            return _cached;
        }
        catch (Exception ex) when (ex is KeyNotFoundException
                                      or System.Runtime.InteropServices.COMException
                                      or InvalidOperationException)
        {
            return FallbackPrice;
        }
    }
}
