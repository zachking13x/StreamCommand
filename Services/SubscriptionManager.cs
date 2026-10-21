using System;
using System.Threading.Tasks;
using Windows.Services.Store;
using WinRT.Interop;

namespace StreamCommand.Services
{
    /// <summary>
    /// Buys the app itself.
    ///
    /// PRICING MODEL (v1.0.29+): Stream Command is a paid app ($7.99 one-time) with a 7-day
    /// Store free trial and NO add-ons. "Buying" therefore means purchasing the app's own
    /// Store product - the full licence that turns a trial into ownership - not an in-app
    /// add-on. The three subscription add-ons this class used to sell (pro_monthly,
    /// pro_annual, pro_lifetime) are retired and stop-selling; licences already bought are
    /// still honoured by EntitlementService.LegacyProductIds.
    /// </summary>
    public static class SubscriptionManager
    {
        /// <summary>
        /// The app's own Store ID, used when the Store cannot name the product dynamically.
        /// Same ID as the Store listing and review URL.
        /// </summary>
        public const string AppStoreId = "9PCVKFCX60C1";

        private static StoreContext? _context;

        public static void Initialize(IntPtr hwnd)
        {
            _context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(_context, hwnd);
        }

        /// <summary>
        /// Shows the Store purchase dialog for the full app licence. On success the
        /// entitlement is refreshed so every gate opens without a restart.
        /// </summary>
        /// <exception cref="InvalidOperationException">Initialize was never called.</exception>
        public static async Task<StorePurchaseStatus> PurchaseAppAsync()
        {
            if (_context == null)
                throw new InvalidOperationException("SubscriptionManager not initialized with window handle.");

            // Prefer the Store's own product id for this app; fall back to the known one so a
            // transient catalog hiccup never blocks a sale.
            string storeId = AppStoreId;
            try
            {
                var product = await _context.GetStoreProductForCurrentAppAsync();
                if (!string.IsNullOrEmpty(product?.Product?.StoreId))
                    storeId = product.Product.StoreId;
            }
            catch { /* fall back to AppStoreId */ }

            StorePurchaseResult result = await _context.RequestPurchaseAsync(storeId);

            if (result.Status is StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased)
            {
                await EntitlementService.RefreshAsync();
                LocalCache.SaveProState(EntitlementService.ActiveProductId);
            }

            return result.Status;
        }
    }
}
