using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class MainWindow : Window
{
    public static Action<string>? NavigateTo { get; private set; }

    private readonly Dictionary<string, Lazy<UserControl>> _viewFactories;

    private static readonly HashSet<string> _validProductIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "pro_monthly", "pro_annual", "pro_lifetime"
    };

    private readonly DispatcherTimer _reminderTimer;
    private readonly HashSet<string> _notifiedEventKeys = new();
    private bool _wasStreaming = false;
    private int  _sessionAutomationsFired = 0;
    private int  _streamStartFollowers    = 0;
    private DateTime? _streamStartTime    = null;

    public MainWindow()
    {
        InitializeComponent();

        var cached = LocalCache.LoadProState();
        if (cached != null && _validProductIds.Contains(cached))
        {
            EntitlementService.IsProPending    = true;
            EntitlementService.ActiveProductId = cached;
        }
        _ = EntitlementService.RefreshAsync();

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
            ["setup-guide"]  = new Lazy<UserControl>(() => new ConnectionSetupView()),
        };

        AutomationEngine.Instance.ReloadFromSettings();

        // OBS session counter + profile pill update
        OBSWebSocketService.Shared.StateChanged += state =>
        {
            Dispatcher.Invoke(() => UpdateTwitchPill());
            if (state == OBSState.Connected)
            {
                var s = SettingsService.Load();
                s.OBSSessionsCompleted++;
                if (!s.OBSEverConnected) s.OBSEverConnected = true;
                SettingsService.Save(s);
                StreamEvents.RaiseUsageUpdated();
            }
        };

        // Stream start/end counters + stream-end summary
        OBSWebSocketService.Shared.StreamingStateChanged += isStreaming =>
        {
            if (!_wasStreaming && isStreaming)
            {
                _streamStartTime       = DateTime.Now;
                _sessionAutomationsFired = 0;
            }
            else if (_wasStreaming && !isStreaming)
            {
                var s = SettingsService.Load();
                s.StreamsCompleted++;
                s.LastStreamDate = DateTime.Now;
                SettingsService.Save(s);
                StreamEvents.RaiseUsageUpdated();
                Dispatcher.Invoke(() => OnStreamEnded(s));
            }
            _wasStreaming = isStreaming;
        };

        // Track session automation count for stream-end summary
        StreamEvents.AutomationFired += (_, _) => _sessionAutomationsFired++;

        // Update PRO badge when entitlement refreshes
        EntitlementService.Refreshed += () => Dispatcher.Invoke(UpdateProBadge);

        TwitchChatService.Shared.StateChanged += _ => Dispatcher.Invoke(UpdateTwitchPill);

        Loaded += async (_, _) =>
        {
            var s = SettingsService.Load();

            if (s.FirstLaunchDate == null)
            {
                s.FirstLaunchDate = DateTime.Now;
                SettingsService.Save(s);
            }

            UpdateSidebarProfile(s);
            UpdateProBadge();
            CheckReviewBanner(s);

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

            var eventSubClientId = !string.IsNullOrWhiteSpace(s.TwitchClientId)
                ? s.TwitchClientId : TwitchOAuthService.ClientId;

            if (!string.IsNullOrWhiteSpace(s.TwitchUsername) &&
                !string.IsNullOrWhiteSpace(s.TwitchChatToken))
            {
                // Chat starts here, not in Chat Monitor, so automation runs from launch.
                _ = TwitchChatService.Shared.StartFromSettingsAsync();
                await EventSubService.Instance.ConnectAsync(
                    s.TwitchUsername, eventSubClientId, s.TwitchChatToken);
                s = SettingsService.Load();
                if (!s.TwitchEverConnected)
                {
                    s.TwitchEverConnected = true;
                    SettingsService.Save(s);
                }
                UpdateTwitchPill();
            }

            s = SettingsService.Load();
            if (!s.OBSEverConnected && !s.SetupNudgeSent
                && s.FirstLaunchDate.HasValue
                && (DateTime.Now - s.FirstLaunchDate.Value).TotalDays >= 1)
            {
                s.SetupNudgeSent = true;
                SettingsService.Save(s);
                ShowToast("StreamCommand is ready",
                    "Connect OBS to take control of your stream — it only takes 30 seconds.");
            }

            foreach (var key in s.NotifiedEventIds)
                _notifiedEventKeys.Add(key);

            CheckPreStreamReminders();
            CheckReEngagementReminders();
        };

        NavList.SelectedIndex = 0;

        NavigateTo = tag =>
        {
            Dispatcher.Invoke(() =>
            {
                var item = NavList.Items.OfType<ListBoxItem>()
                                  .FirstOrDefault(i => i.Tag?.ToString() == tag);
                if (item != null) NavList.SelectedItem = item;
            });
        };

        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _reminderTimer.Tick += (_, _) => { CheckPreStreamReminders(); CheckReEngagementReminders(); };
        _reminderTimer.Start();
    }

    // ── Sidebar profile ───────────────────────────────────────────────────────

    private void UpdateSidebarProfile(AppSettings s)
    {
        var username = string.IsNullOrWhiteSpace(s.TwitchUsername) ? "Not connected" : s.TwitchUsername;
        SidebarUsername.Text = username;
        UserInitials.Text    = username == "Not connected" ? "?" :
                               username.Length >= 2 ? username[..2].ToUpper() : username[..1].ToUpper();
    }

    // Driven by the live chat state, not "a token is saved". The old rule showed a green
    // dot over an expired login, so a streamer could go live with automation dead.
    private void UpdateTwitchPill()
    {
        var s = SettingsService.Load();
        bool linked = !string.IsNullOrWhiteSpace(s.TwitchChatToken) && s.TwitchEverConnected;

        var (dotKey, text, live) = (linked, TwitchChatService.Shared.State) switch
        {
            (false, _)                                    => ("MutedText",     "Not connected",    false),
            (_, TwitchChatState.Connected)                => ("SuccessBrush",  s.TwitchUsername,   true),
            (_, TwitchChatState.AuthenticationFailed)     => ("DangerBrush",   "Reconnect Twitch", true),
            (_, TwitchChatState.ChannelUnavailable)       => ("DangerBrush",   "Channel unavailable", true),
            (_, TwitchChatState.Connecting or TwitchChatState.Reconnecting)
                                                          => ("WarningBrush",  "Connecting…",      true),
            _                                             => ("MutedText",     s.TwitchUsername,   true),
        };

        TwitchDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dotKey);
        TwitchPillText.Text = text;
        TwitchPillText.SetResourceReference(TextBlock.ForegroundProperty, live ? "PrimaryText" : "MutedText");
    }

    private void UpdateProBadge()
    {
        ProBadge.Visibility = EntitlementService.IsPro ? Visibility.Visible : Visibility.Collapsed;
        if (EntitlementService.IsPro)
            SidebarRank.Text = "Pro";
    }

    // ── Stream-end summary ────────────────────────────────────────────────────

    private void OnStreamEnded(AppSettings s)
    {
        var duration = _streamStartTime.HasValue
            ? DateTime.Now - _streamStartTime.Value
            : TimeSpan.Zero;

        var dlg = new StreamSummaryDialog(
            duration,
            automationsFired: _sessionAutomationsFired,
            streamsCompleted: s.StreamsCompleted)
        { Owner = this };
        dlg.ShowDialog();

        // First-stream nudge toast after dialog dismissed
        s = SettingsService.Load();
        if (s.StreamsCompleted == 1 && !EntitlementService.IsPro && !s.FirstStreamNudgeSent)
        {
            s.FirstStreamNudgeSent = true;
            SettingsService.Save(s);
            ShowToast(
                "First stream complete!",
                $"Your automation fired {_sessionAutomationsFired} times. Upgrade to Pro to go unlimited.");
        }

        _sessionAutomationsFired = 0;
        _streamStartTime         = null;
    }

    // ── Review banner ─────────────────────────────────────────────────────────

    private void CheckReviewBanner(AppSettings s)
    {
        if (EntitlementService.IsPro) return;
        if (s.ReviewPromptShown) return;
        if (s.StreamsCompleted < 5) return;
        if (s.ReviewPromptDate.HasValue &&
            (DateTime.Now - s.ReviewPromptDate.Value).TotalDays < 14) return;

        ReviewBanner.Visibility   = Visibility.Visible;
        ReviewBannerRow.Height    = new GridLength(52);
    }

    private void ReviewLeave_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Load();
        s.ReviewPromptShown = true;
        SettingsService.Save(s);
        HideReviewBanner();
        try { Process.Start(new ProcessStartInfo(
            "ms-windows-store://review/?ProductId=9PCVKFCX60C1") { UseShellExecute = true }); }
        catch { }
    }

    private void ReviewLater_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Load();
        s.ReviewPromptDate = DateTime.Now;
        SettingsService.Save(s);
        HideReviewBanner();
    }

    private void HideReviewBanner()
    {
        ReviewBanner.Visibility = Visibility.Collapsed;
        ReviewBannerRow.Height  = new GridLength(0);
    }

    // ── Nav ───────────────────────────────────────────────────────────────────

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = NavList.SelectedItem as ListBoxItem;
        var tag  = item?.Tag?.ToString();

        if (tag == "whats-new")
        {
            var win = new WhatsNewWindow { Owner = this };
            win.ShowDialog();
            if (e.RemovedItems.Count > 0)
                NavList.SelectedItem = e.RemovedItems[0];
            return;
        }

        if (tag != null && _viewFactories.TryGetValue(tag, out var factory))
            MainContent.Content = factory.Value;
    }

    // ── Pre-stream reminders ──────────────────────────────────────────────────

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
                var key = ev.Id;
                if (_notifiedEventKeys.Contains(key)) continue;
                _notifiedEventKeys.Add(key);
                s.NotifiedEventIds.Add(key);
                saved = true;
                var mins = (int)(ev.When - now).TotalMinutes;
                ShowToast($"\U0001f4fa  {ev.Title} starting soon",
                    $"Your stream starts in {mins} minutes — run your pre-stream checklist.");
            }
            if (saved) SettingsService.Save(s);
        }
        catch { }
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
            ShowToast("Ready to stream?",
                "StreamCommand is standing by — your checklist and automation are set up and ready.");
        }
        catch { }
    }

    private static void ShowToast(string title, string body) => ToastHelper.Show(title, body);
}
