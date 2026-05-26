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
                    // Outside Store context — default to free tier so no one can bypass
                    // by simply not being in the Store environment.
                    // Developers can set env var STREAMCMD_PRO_OVERRIDE=1 for local testing.
#if DEBUG
                    IsPro = Environment.GetEnvironmentVariable("STREAMCMD_PRO_OVERRIDE") == "1";
#else
                    IsPro = false;
#endif
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
                // GetAppLicenseAsync threw — treat as free tier in production.
#if DEBUG
                IsPro = Environment.GetEnvironmentVariable("STREAMCMD_PRO_OVERRIDE") == "1";
#else
                IsPro = false;
#endif
                Refreshed?.Invoke();
            }
        }
    }
}
