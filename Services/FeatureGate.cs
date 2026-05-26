using System.Collections.Generic;

namespace StreamCommand.Services
{
    /// <summary>
    /// Granular feature gating.
    ///
    /// Usage:
    ///   FeatureGate.Has("analytics-history")   → false for free users
    ///   FeatureGate.Has("dashboard")            → true for everyone
    ///
    /// Free-tier features are always available regardless of subscription.
    /// Pro-only features require EntitlementService.IsPro == true.
    /// Any unrecognised feature key is treated as Pro-only (safe default).
    /// </summary>
    public static class FeatureGate
    {
        /// <summary>Shorthand — true when the user has an active Pro subscription.</summary>
        public static bool Pro => EntitlementService.IsPro;

        // Features available to free users (no subscription required)
        private static readonly HashSet<string> _freeFeatures = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "audio-meters",         // real-time OBS level meters — every streamer needs mic confirmation
            "dashboard",
            "live-control",
            "pre-stream",           // checklist view/read (edit is Pro)
            "chat-monitor",
            "automation-basic",     // up to 3 enabled rules
            "planner-basic",        // up to 3 scheduled streams
            "analytics-live",       // live/current session data only
            "growth",
            "quick-launch-basic",   // up to 5 saved apps
            "tools-hub",
            "settings",
            "whats-new",
        };

        // Pro-only features — explicitly listed for documentation; anything not in
        // _freeFeatures also falls through to the Pro check.
        private static readonly HashSet<string> _proFeatures = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "analytics-history",        // 30-day chart history
            "automation-unlimited",     // more than 3 active rules
            "live-preview",             // OBS Virtual Camera feed inside the app
            "planner-unlimited",        // more than 3 scheduled streams
            "pre-stream-edit",          // add/remove checklist items
            "quick-launch-unlimited",   // more than 5 saved apps
            "multi-platform",           // YouTube integration
            "stream-alerts",            // on-screen alert overlay
            "priority-support",
        };

        /// <summary>
        /// Returns true if the current user can access <paramref name="feature"/>.
        /// Free-tier features always return true.
        /// Pro-only features return true only when the user has Pro.
        /// Unknown feature keys are Pro-gated by default.
        /// </summary>
        public static bool Has(string feature)
        {
            if (_freeFeatures.Contains(feature)) return true;
            return Pro;
        }

        /// <summary>
        /// Returns true when <paramref name="feature"/> is a Pro feature AND the user does
        /// NOT have Pro — i.e. "should I show an upgrade prompt here?"
        /// </summary>
        public static bool IsLocked(string feature) => !Has(feature);
    }
}
