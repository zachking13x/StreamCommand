using System;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace StreamCommand.Services
{
    public static class EntitlementService
    {
        public static bool IsPro { get; set; }

        /// <summary>
        /// True when a valid cached product ID was found at startup, but the Store has not
        /// yet confirmed the subscription. Views should NOT gate on this — wait for Refreshed.
        /// </summary>
        public static bool IsProPending { get; set; }

        public static string? ActiveProductId { get; set; }

        /// <summary>
        /// Fired after <see cref="RefreshAsync"/> has set the definitive <see cref="IsPro"/> value.
        /// Views that gate on Pro should subscribe here and re-evaluate when this fires.
        /// </summary>
        public static event Action? Refreshed;

        // Lazy + guarded: GetDefault() throws COMException when the app is not in a
        // packaged/Store context (e.g. sideloaded during development).  We catch that
        // here so the static initializer never faults the whole class.
        private static StoreContext? _context;

        private static StoreContext? EnsureContext()
        {
            if (_context != null) return _context;
            try { _context = StoreContext.GetDefault(); }
            catch { /* Not running inside a packaged / Store context — leave null. */ }
            return _context;
        }

        private static readonly string[] ProductIds =
        {
            "pro_monthly",
            "pro_annual",
            "pro_lifetime"
        };

        public static async Task RefreshAsync()
        {
            try
            {
                var ctx = EnsureContext();
                if (ctx == null)
                {
                    // Outside Store context (dev / sideloaded) — trust the pending cache value
                    // so the developer can test Pro features without a real subscription.
                    IsPro = IsProPending;
                    Refreshed?.Invoke();
                    return;
                }

                StoreAppLicense license = await ctx.GetAppLicenseAsync();

                foreach (var id in ProductIds)
                {
                    if (license.AddOnLicenses.TryGetValue(id, out StoreLicense? addon))
                    {
                        if (addon.IsActive)
                        {
                            IsPro           = true;
                            ActiveProductId = id;
                            LocalCache.SaveProState(id);
                            Refreshed?.Invoke();
                            return;
                        }
                    }
                }

                // No active subscription found — clear Pro
                IsPro           = false;
                ActiveProductId = null;
                LocalCache.SaveProState(null);
                Refreshed?.Invoke();
            }
            catch
            {
                // GetAppLicenseAsync throws COMException when the app runs outside the Store
                // context (e.g. sideloaded during development). Fall back to the pending cache value.
                IsPro = IsProPending;
                Refreshed?.Invoke();
            }
        }
    }
}
