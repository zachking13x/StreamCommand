using System;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace StreamCommand.Services
{
    /// <summary>
    /// Owns "is this user entitled to the full app?".
    ///
    /// PRICING MODEL (v1.0.29+): Stream Command is a **paid app** ($7.99 one-time) with a
    /// 7-day Store free trial and NO add-ons. Owning the app IS the entitlement — there is
    /// no separate Pro purchase, and every feature is included.
    ///
    /// FAIL-OPEN CONTRACT — read this before changing anything here.
    /// If the Store cannot be reached, or GetAppLicenseAsync throws, or we are outside a
    /// Store context, we grant access. A paid app MUST NOT lock out someone who already
    /// bought it just because the licence check failed (offline, Store outage, sideload).
    /// Failing closed here is exactly the CleanAimTracker 1.0.103 lockout bug — a missing
    /// licence looked like an expired trial and paying customers lost the app.
    /// Note this is deliberately the OPPOSITE of CredentialProtection, which fails closed:
    /// leaking a secret is worse than losing it, but locking out a buyer is worse than
    /// letting an edge case through.
    /// </summary>
    public static class EntitlementService
    {
        /// <summary>
        /// True when the user may use every feature — they own the app, are inside the
        /// free trial, or we could not verify and defaulted to granting access.
        /// Name kept as IsPro so existing call sites keep working; there is no longer a
        /// separate "Pro" product.
        /// </summary>
        public static bool IsPro { get; set; }

        /// <summary>Clearer alias for <see cref="IsPro"/> under the paid-app model.</summary>
        public static bool IsEntitled => IsPro;

        /// <summary>True while the user is inside the Store's free trial rather than owning the app.</summary>
        public static bool IsTrial { get; private set; }

        /// <summary>When the free trial ends, if the Store reported it.</summary>
        public static DateTimeOffset? TrialExpiration { get; private set; }

        /// <summary>Whole days left in the trial, or null when not on a trial / unknown.</summary>
        public static int? TrialDaysRemaining
        {
            get
            {
                if (!IsTrial || TrialExpiration is not DateTimeOffset end) return null;
                var days = (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalDays);
                return days < 0 ? 0 : days;
            }
        }

        /// <summary>
        /// True when a cached entitlement was found at startup but the Store has not yet
        /// confirmed it. Views should NOT gate on this — wait for <see cref="Refreshed"/>.
        /// </summary>
        public static bool IsProPending { get; set; }

        public static string? ActiveProductId { get; set; }

        /// <summary>
        /// Fired after <see cref="RefreshAsync"/> has set the definitive <see cref="IsPro"/> value.
        /// Views that gate on entitlement should subscribe here and re-evaluate.
        /// </summary>
        public static event Action? Refreshed;

        /// <summary>Raises <see cref="Refreshed"/> from outside the class.</summary>
        public static void RaiseRefreshed() => Refreshed?.Invoke();

        // Cache markers written to subscription.json so an offline launch still works.
        public const string OwnedMarker = "app_owned";
        public const string TrialMarker = "app_trial";

        // Lazy + guarded: GetDefault() throws COMException when the app is not in a
        // packaged/Store context (e.g. sideloaded during development).
        private static StoreContext? _context;

        private static StoreContext? EnsureContext()
        {
            if (_context != null) return _context;
            try { _context = StoreContext.GetDefault(); }
            catch { /* Not in a packaged / Store context — leave null. */ }
            return _context;
        }

        // Legacy subscription add-ons, retired when the app became paid. They are no longer
        // sold, but anyone who bought one before the switch must keep full access forever.
        private static readonly string[] LegacyProductIds =
        {
            "9P278M24CMND",   // pro_monthly
            "9ND71MP15Q0C",   // pro_annual
            "9N5XMTJ071XR",   // pro_lifetime
        };

        public static async Task RefreshAsync()
        {
#if DEBUG
            // Developer unlock — DEBUG-ONLY so a hand-edited settings.json can never
            // bypass anything in a shipped build. Compiled out of Release entirely.
            if (SettingsService.Load().DevProUnlock)
            {
                Grant("dev_unlock", trial: false, expiry: null, cache: false);
                return;
            }
#endif
            try
            {
                var ctx = EnsureContext();
                if (ctx == null)
                {
                    // No Store context at all (sideloaded dev build, or Store subsystem
                    // unavailable). Fail OPEN — see the class-level contract.
                    Grant("no_store_context", trial: false, expiry: null, cache: false);
                    return;
                }

                StoreAppLicense license = await ctx.GetAppLicenseAsync();

                // 1) Grandfathered subscribers from the pre-paid era keep everything.
                foreach (var id in LegacyProductIds)
                {
                    if (license.AddOnLicenses.TryGetValue(id, out StoreLicense? addon) && addon.IsActive)
                    {
                        Grant(id, trial: false, expiry: null, cache: true);
                        return;
                    }
                }

                // 2) No active app licence => trial expired or not licensed.
                if (!license.IsActive)
                {
                    IsPro           = false;
                    IsTrial         = false;
                    TrialExpiration = null;
                    ActiveProductId = null;
                    LocalCache.SaveProState(null);
                    Refreshed?.Invoke();
                    return;
                }

                // 3) Active licence — either a bought app or a running trial.
                DateTimeOffset? expiry = null;
                if (license.IsTrial)
                {
                    try { expiry = license.ExpirationDate; } catch { /* not always populated */ }
                }

                Grant(license.IsTrial ? TrialMarker : OwnedMarker,
                      trial: license.IsTrial, expiry: expiry, cache: true);
            }
            catch
            {
                // Licence check failed (offline, Store outage, COM error). Fail OPEN so a
                // paying customer is never locked out of an app they already bought.
                Grant("licence_check_failed", trial: false, expiry: null, cache: false);
            }
        }

        private static void Grant(string productId, bool trial, DateTimeOffset? expiry, bool cache)
        {
            IsPro           = true;
            IsTrial         = trial;
            TrialExpiration = expiry;
            ActiveProductId = productId;
            if (cache) LocalCache.SaveProState(productId);
            Refreshed?.Invoke();
        }
    }
}
