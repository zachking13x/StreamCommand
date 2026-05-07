using System;
using System.Threading.Tasks;
using Windows.Services.Store;
using WinRT.Interop;

namespace StreamCommand.Services
{
    public static class SubscriptionManager
    {
        private static StoreContext? _context;

        public static void Initialize(IntPtr hwnd)
        {
            _context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(_context, hwnd);
        }

        public static async Task<bool> PurchaseAsync(string productId)
        {
            if (_context == null)
                throw new InvalidOperationException("SubscriptionManager not initialized with window handle.");

            StorePurchaseResult result = await _context.RequestPurchaseAsync(productId);

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
