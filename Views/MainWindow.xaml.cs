using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class MainWindow : Window
{
    // Any view can call MainWindow.NavigateTo("live-control") to switch pages
    public static Action<string>? NavigateTo { get; private set; }

    // Views are created on first access — not all 11 up front
    private readonly Dictionary<string, Lazy<UserControl>> _viewFactories;

    // Known valid product IDs — used to validate the local cache
    private static readonly HashSet<string> _validProductIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "pro_monthly",
        "pro_annual",
        "pro_lifetime"
    };

    // Pre-stream reminder toast state
    private readonly DispatcherTimer _reminderTimer;
    private readonly HashSet<string> _notifiedEventKeys = new();

    // Track prior OBS streaming state to detect true→false transitions
    private bool _wasStreaming = false;

    public MainWindow()
    {
        InitializeComponent();

        // ── T3: Validate cache before trusting it ──────────────────────────────
        // Set IsProPending (not IsPro) from cache — IsPro is only set after Store confirms.
        // This closes the exploit where any user could create a subscription.json and get Pro.
        var cached = LocalCache.LoadProState();
        if (cached != null && _validProductIds.Contains(cached))
        {
            EntitlementService.IsProPending  = true;
            EntitlementService.ActiveProductId = cached;
            // IsPro intentionally NOT set here — wait for RefreshAsync
        }

        // Refresh entitlements from Microsoft Store (async fire-and-forget)
        _ = EntitlementService.RefreshAsync();

        // ── T5: Lazy view factories — created on first navigation ──────────────
        _viewFactories = new Dictionary<string, Lazy<UserControl>>
        {
            ["dashboard"]    = new Lazy<UserControl>(() => new DashboardView()),
            ["live-control"] = new Lazy<UserControl>(() => new LiveControlView()),
            ["pre-stream"]   = new Lazy<UserControl>(() => new PreStreamView()),
            ["chat-monitor"] = new Lazy<UserControl>(() => new ChatMonitorView()),
            ["automation"]   = new Lazy<UserControl>(() => new AutomationView()),
            ["planner"]      = new Lazy<UserControl>(() => new PlannerView()),
            ["analytics"]    = new Lazy<UserControl>(() => new AnalyticsView()),
            ["growth"]       = new Lazy<UserControl>(() => new GrowthView()),
            ["quick-launch"] = new Lazy<UserControl>(() => new QuickLaunchView()),
            ["tools-hub"]    = new Lazy<UserControl>(() => new ToolsHubView()),
            ["settings"]     = new Lazy<UserControl>(() => new SettingsView()),
        };

        // ── T1: Start AutomationEngine with persisted rules ────────────────────
        AutomationEngine.Instance.ReloadFromSettings();

        // ── Usage counters — OBS sessions ─────────────────────────────────────
        OBSWebSocketService.Shared.StateChanged += state =>
        {
            if (state == OBSState.Connected)
            {
                var s = SettingsService.Load();
                s.OBSSessionsCompleted++;
                if (!s.OBSEverConnected) s.OBSEverConnected = true;
                SettingsService.Save(s);
                StreamEvents.RaiseUsageUpdated();
            }
        };

        // ── Usage counters — streams completed ────────────────────────────────
        OBSWebSocketService.Shared.StreamingStateChanged += isStreaming =>
        {
            if (_wasStreaming && !isStreaming)
            {
                var s = SettingsService.Load();
                s.StreamsCompleted++;
                s.LastStreamDate = DateTime.Now;
                SettingsService.Save(s);
                StreamEvents.RaiseUsageUpdated();
            }
            _wasStreaming = isStreaming;
        };

        // ── Startup async work ────────────────────────────────────────────────
        Loaded += async (_, _) =>
        {
            var s = SettingsService.Load();

            // ── First launch date ─────────────────────────────────────────────
            if (s.FirstLaunchDate == null)
            {
                s.FirstLaunchDate = DateTime.Now;
                SettingsService.Save(s);
            }

            // Validate OAuth token — silently refresh if expired
            if (!string.IsNullOrWhiteSpace(s.TwitchChatToken))
            {
                var validation = await TwitchOAuthService.ValidateTokenAsync(s.TwitchChatToken);
                if (!validation.IsValid && !string.IsNullOrWhiteSpace(s.TwitchRefreshToken))
                {
                    var refreshed = await TwitchOAuthService.RefreshTokenAsync(s.TwitchRefreshToken);
                    if (refreshed.HasValue)
                    {
                        s.TwitchChatToken    = refreshed.Value.access;
                        s.TwitchRefreshToken = refreshed.Value.refresh;
                        SettingsService.Save(s);
                    }
                }
            }

            // Start EventSub for follow events.
            // Fall back to the embedded client ID if the stored one is missing —
            // this covers users who set up on a very old version before it was saved.
            var eventSubClientId = !string.IsNullOrWhiteSpace(s.TwitchClientId)
                ? s.TwitchClientId
                : TwitchOAuthService.ClientId;

            if (!string.IsNullOrWhiteSpace(s.TwitchUsername) &&
                !string.IsNullOrWhiteSpace(s.TwitchChatToken))
            {
                await EventSubService.Instance.ConnectAsync(
                    s.TwitchUsername, eventSubClientId, s.TwitchChatToken);

                // Mark Twitch as ever connected on successful attempt
                s = SettingsService.Load();
                if (!s.TwitchEverConnected)
                {
                    s.TwitchEverConnected = true;
                    SettingsService.Save(s);
                }
            }

            // ── Setup nudge — fires once if OBS never connected after 1 day ──
            s = SettingsService.Load();
            if (!s.OBSEverConnected
                && !s.SetupNudgeSent
                && s.FirstLaunchDate.HasValue
                && (DateTime.Now - s.FirstLaunchDate.Value).TotalDays >= 1)
            {
                s.SetupNudgeSent = true;
                SettingsService.Save(s);
                ShowToast(
                    "StreamCommand is ready",
                    "Connect OBS to take control of your stream — it only takes 30 seconds.");
            }

            // Load persisted reminder keys (now uses stable PlannerEvent.Id)
            foreach (var key in s.NotifiedEventIds)
                _notifiedEventKeys.Add(key);

            // Check immediately — timer handles the recurring 30-minute checks
            CheckPreStreamReminders();
            CheckReEngagementReminders();
        };

        NavList.SelectedIndex = 0;

        // Wire up the static NavigateTo helper
        NavigateTo = tag =>
        {
            var item = NavList.Items.OfType<ListBoxItem>()
                              .FirstOrDefault(i => i.Tag?.ToString() == tag);
            if (item != null) NavList.SelectedItem = item;
        };

        // Pre-stream reminder timer — fires every 30 minutes
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _reminderTimer.Tick += (_, _) => { CheckPreStreamReminders(); CheckReEngagementReminders(); };
        _reminderTimer.Start();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = NavList.SelectedItem as ListBoxItem;
        var tag  = item?.Tag?.ToString();

        if (tag == "whats-new")
        {
            var win = new WhatsNewWindow { Owner = this };
            win.ShowDialog();
            // Restore previous selection so the item doesn't stay highlighted
            if (e.RemovedItems.Count > 0)
                NavList.SelectedItem = e.RemovedItems[0];
            return;
        }

        if (tag != null && _viewFactories.TryGetValue(tag, out var factory))
            MainContent.Content = factory.Value;   // Lazy<T>.Value creates on first access
    }

    private void UpgradePro_Click(object sender, MouseButtonEventArgs e)
    {
        var s   = SettingsService.Load();
        var ctx = new UsageContext
        {
            StreamsCompleted     = s.StreamsCompleted,
            AutomationFiredCount = s.AutomationFiredCount,
            ProGateHitCount      = s.ProGateHitCount,
        };
        var win = new ProUpgradeWindow(ctx) { Owner = this };
        win.ShowDialog();
    }

    // ── Pre-stream reminder toasts ────────────────────────────────────────────

    private void CheckPreStreamReminders()
    {
        try
        {
            var s   = SettingsService.Load();
            var now = DateTime.Now;
            bool saved = false;

            foreach (var ev in s.PlannerEvents)
            {
                if (ev.When <= now || ev.When > now.AddMinutes(60)) continue;

                var key = ev.Id;   // stable Guid — never collides on rename or duplicate title
                if (_notifiedEventKeys.Contains(key)) continue;

                _notifiedEventKeys.Add(key);
                s.NotifiedEventIds.Add(key);
                saved = true;

                var minutesUntil = (int)(ev.When - now).TotalMinutes;
                ShowToast(
                    $"\U0001f4fa  {ev.Title} starting soon",
                    $"Your stream starts in {minutesUntil} minutes — run your pre-stream checklist.");
            }

            if (saved) SettingsService.Save(s);
        }
        catch { /* Never crash the UI thread from timer */ }
    }

    private void CheckReEngagementReminders()
    {
        try
        {
            var s = SettingsService.Load();
            if (EntitlementService.IsPro) return;
            if (s.LastStreamDate == null) return;
            if ((DateTime.Now - s.LastStreamDate.Value).TotalDays < 7) return;
            if (s.LastReEngagementToast.HasValue &&
                (DateTime.Now - s.LastReEngagementToast.Value).TotalDays < 7) return;

            s.LastReEngagementToast = DateTime.Now;
            SettingsService.Save(s);
            ShowToast(
                "Ready to stream?",
                "StreamCommand is standing by — your checklist and automation are set up and ready.");
        }
        catch { }
    }

    private static void ShowToast(string title, string body)
        => ToastHelper.Show(title, body);
}
