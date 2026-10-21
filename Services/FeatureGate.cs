using System.Collections.Generic;

namespace StreamCommand.Services
{
    /// <summary>
    /// Feature gating under the paid-app model (v1.0.29+).
    ///
    /// Stream Command is a paid app with a 7-day Store trial and no add-ons, so there is no
    /// "Pro tier" any more: if the user is entitled (owns the app, or is inside the trial)
    /// they get EVERY feature. The Store listing promises exactly that, so this must not
    /// hold anything back from a buyer.
    ///
    /// The only time a gate closes is an expired trial on a machine that never bought the
    /// app. Even then we keep the navigation-level features working rather than bricking the
    /// window, so the user sees a purchase prompt instead of a broken shell — a dead app
    /// earns a one-star review, a prompt earns a sale.
    /// </summary>
    public static class FeatureGate
    {
        /// <summary>True when the user is entitled to the full app (owned or in trial).</summary>
        public static bool Pro => EntitlementService.IsPro;

        /// <summary>
        /// Features that stay available even to an expired, unpurchased trial so the app
        /// remains navigable and can present the buy prompt. NOT a free tier — an entitled
        /// user gets these plus everything else.
        /// </summary>
        private static readonly HashSet<string> _alwaysAvailable = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "dashboard",
            "settings",
            "whats-new",
            "tools-hub",
            "setup-guide",
        };

        /// <summary>
        /// Returns true if the current user can use <paramref name="feature"/>.
        /// Entitled users get everything; otherwise only the navigation set above.
        /// </summary>
        public static bool Has(string feature)
            => Pro || _alwaysAvailable.Contains(feature);

        /// <summary>
        /// True when <paramref name="feature"/> is unavailable — i.e. "should I show a
        /// purchase prompt here?".
        /// </summary>
        public static bool IsLocked(string feature) => !Has(feature);
    }
}
