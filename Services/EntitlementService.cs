using System.Threading.Tasks;
using Windows.Services.Store;

namespace StreamCommand.Services
{
    public static class EntitlementService
    {
        public static bool IsPro { get; set; }
        public static string? ActiveProductId { get; set; }

        private static StoreContext _context = StoreContext.GetDefault();

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
                StoreAppLicense license = await _context.GetAppLicenseAsync();

                foreach (var id in ProductIds)
                {
                    if (license.AddOnLicenses.TryGetValue(id, out StoreLicense addon))
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
