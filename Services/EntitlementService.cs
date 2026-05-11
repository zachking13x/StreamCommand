using System.Threading.Tasks;
using Windows.Services.Store;

namespace StreamCommand.Services
{
    public static class EntitlementService
    {
        public static bool IsPro { get; set; }
        public static string? ActiveProductId { get; set; }

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
                if (ctx == null) return;          // outside Store — keep cached value
                StoreAppLicense license = await ctx.GetAppLicenseAsync();

                foreach (var id in ProductIds)
                {
                    if (license.AddOnLicenses.TryGetValue(id, out StoreLicense? addon))
                    {
                        if (addon.IsActive)
                        {
                            IsPro = true;
                            ActiveProductId = id;
                            return;
                        }
                    }
                }

                IsPro = false;
                ActiveProductId = null;
            }
            catch
            {
                // GetAppLicenseAsync throws COMException when the app runs outside the Store
                // context (e.g. sideloaded during development). Leave IsPro at its cached value.
            }
        }
    }
}
