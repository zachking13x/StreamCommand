using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public MainWindow()
    {
        InitializeComponent();

        // ── T3: Validate cache before trusting it ──────────────────────────
        // Only grant Pro from cache if the product ID is a known valid one.
        // RefreshAsync() runs async below and will correct IsPro from the Store.
        var cached = LocalCache.LoadProState();
        if (cached != null && _validProductIds.Contains(cached))
        {
            EntitlementService.IsPro          = true;
            EntitlementService.ActiveProductId = cached;
        }

        // Refresh entitlements from Microsoft Store (async fire-and-forget)
        _ = EntitlementService.RefreshAsync();

        // ── T5: Lazy view factories — created on first navigation ──────────
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

        // ── T1: Start AutomationEngine with persisted rules ────────────────
        AutomationEngine.Instance.ReloadFromSettings();

        NavList.SelectedIndex = 0;

        // Wire up the static NavigateTo helper
        NavigateTo = tag =>
        {
            var item = NavList.Items.OfType<ListBoxItem>()
                              .FirstOrDefault(i => i.Tag?.ToString() == tag);
            if (item != null) NavList.SelectedItem = item;
        };
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
        var win = new ProUpgradeWindow { Owner = this };
        win.ShowDialog();
    }
}
