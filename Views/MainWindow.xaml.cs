using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StreamCommand.Views;

public partial class MainWindow : Window
{
    // Any view can call MainWindow.NavigateTo("live-control") to switch pages
    public static Action<string>? NavigateTo { get; private set; }

    private readonly Dictionary<string, UserControl> _views = new();

    public MainWindow()
    {
        InitializeComponent();

        // Load cached subscription state
        var cached = Services.LocalCache.LoadProState();
        if (cached != null)
        {
            Services.EntitlementService.IsPro = true;
            Services.EntitlementService.ActiveProductId = cached;
        }

        // Refresh entitlements from Microsoft Store (async fire-and-forget)
        _ = Services.EntitlementService.RefreshAsync();


        // Pre-create all views once
        _views["dashboard"]    = new DashboardView();
        _views["live-control"] = new LiveControlView();
        _views["pre-stream"]   = new PreStreamView();
        _views["chat-monitor"] = new ChatMonitorView();
        _views["automation"]   = new AutomationView();
        _views["planner"]      = new PlannerView();
        _views["analytics"]    = new AnalyticsView();
        _views["growth"]       = new GrowthView();
        _views["quick-launch"] = new QuickLaunchView();
        _views["tools-hub"]    = new ToolsHubView();
        _views["settings"]     = new SettingsView();

        NavList.SelectedIndex = 0;

        // Wire up the static NavigateTo helper
        NavigateTo = tag =>
        {
            var item = NavList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
            if (item != null) NavList.SelectedItem = item;
        };
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = NavList.SelectedItem as ListBoxItem;
        var tag = item?.Tag?.ToString();

        if (tag == "whats-new")
        {
            // Open changelog as a dialog, then deselect so the item doesn't stay highlighted
            var win = new WhatsNewWindow { Owner = this };
            win.ShowDialog();
            // Restore previous selection
            if (e.RemovedItems.Count > 0)
                NavList.SelectedItem = e.RemovedItems[0];
            return;
        }

        if (tag != null && _views.TryGetValue(tag, out var view))
            MainContent.Content = view;
    }
    private void UpgradePro_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new ProUpgradeWindow();
        win.Owner = this;
        win.ShowDialog();
    }





}
