using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Services.Store;
using WinRT.Interop;

namespace StreamCommand.Services
{
    public static class SubscriptionManager
    {
        private static StoreContext? _context;

        // Map the human-readable InAppOfferToken → Microsoft Store ID (9NXXXXX).
        // RequestPurchaseAsync requires the Store ID, NOT the InAppOfferToken.
        private static readonly Dictionary<string, string> _tokenToStoreId = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pro_monthly"]  = "9P278M24CMND",
            ["pro_annual"]   = "9ND71MP15Q0C",
            ["pro_lifetime"] = "9N5XMTJ071XR",
        };

        public static void Initialize(IntPtr hwnd)
        {
            _context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(_context, hwnd);
        }

        public static async Task<bool> PurchaseAsync(string productToken)
        {
            if (_context == null)
                throw new InvalidOperationException("SubscriptionManager not initialized with window handle.");

            // Resolve InAppOfferToken → Store ID; pass through if already a Store ID
            var storeId = _tokenToStoreId.TryGetValue(productToken, out var mapped) ? mapped : productToken;

            StorePurchaseResult result = await _context.RequestPurchaseAsync(storeId);

            if (result.Status == StorePurchaseStatus.Succeeded ||
                result.Status == StorePurchaseStatus.AlreadyPurchased)
            {
                await EntitlementService.RefreshAsync();
                LocalCache.SaveProState(EntitlementService.ActiveProductId);
                return true;
            }

            return false;
        }
    }
}
