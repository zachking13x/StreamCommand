using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreamCommand.Models;
using StreamCommand.Services;

namespace StreamCommand.Views;

public class AutomationCategory
{
    public string Category { get; set; } = "";
    public ObservableCollection<AutomationRule> Rules { get; set; } = new();
}

public partial class AutomationView : UserControl
{
    private const int FreeRuleLimit = 3;

    // The live list — bound to the UI, persisted on every change
    private ObservableCollection<AutomationRule> _rules = new();

    // Guard against re-entrant saves when we programmatically revert a toggle
    private bool _suspendSave;

    public AutomationView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadRules();
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    private void LoadRules()
    {
        _suspendSave = true;

        var s = SettingsService.Load();

        // Merge persisted rules with any default seeds that aren't saved yet
        var savedIds  = s.AutomationRules.Select(r => r.Id).ToHashSet();
        var defaults  = new AppSettings().AutomationRules;
        foreach (var def in defaults)
            if (!savedIds.Contains(def.Id))
                s.AutomationRules.Add(def);

        _rules.Clear();
        foreach (var rule in s.AutomationRules)
        {
            _rules.Add(rule);
            rule.PropertyChanged += (_, _) => OnRuleToggled(rule);
        }

        RefreshView();
        _suspendSave = false;
    }

    private void SaveRules()
    {
        if (_suspendSave) return;
        var s = SettingsService.Load();
        s.AutomationRules = _rules.ToList();
        SettingsService.Save(s);
        AutomationEngine.Instance.ReloadFromSettings();
    }

    private void RefreshView()
    {
        bool isPro = EntitlementService.IsPro;
        FreeLimitBanner.Visibility = !isPro ? Visibility.Visible : Visibility.Collapsed;

        var categories = _rules
            .GroupBy(r => r.Category)
            .Select(g => new AutomationCategory
            {
                Category = g.Key,
                Rules    = new ObservableCollection<AutomationRule>(g)
            })
            .ToList();

        CategoriesControl.ItemsSource = categories;
    }

    // ── Toggle enforcement ────────────────────────────────────────────────────

    private void OnRuleToggled(AutomationRule rule)
    {
        if (_suspendSave) return;

        // Free tier: block enabling a rule beyond the limit
        if (rule.IsEnabled && !EntitlementService.IsPro)
        {
            int enabledCount = _rules.Count(r => r.IsEnabled);
            if (enabledCount > FreeRuleLimit)
            {
                _suspendSave = true;
                rule.IsEnabled = false;   // revert
                _suspendSave = false;

                var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
                win.ShowDialog();
                return;
            }
        }

        SaveRules();
    }

    // ── New Rule form ─────────────────────────────────────────────────────────

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        if (!EntitlementService.IsPro && _rules.Count(r => r.IsEnabled) >= FreeRuleLimit)
        {
            var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
            win.ShowDialog();
            return;
        }

        NewRuleForm.Visibility   = Visibility.Visible;
        TriggerTypeCombo.SelectedIndex = 0;
        CommandKeywordPanel.Visibility = Visibility.Collapsed;
        ResponseInput.Text       = "";
        CommandKeywordInput.Text = "";
    }

    private void CancelNewRule_Click(object sender, RoutedEventArgs e)
        => NewRuleForm.Visibility = Visibility.Collapsed;

    private void TriggerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandKeywordPanel == null) return;
        var item = TriggerTypeCombo.SelectedItem as ComboBoxItem;
        CommandKeywordPanel.Visibility =
            item?.Tag?.ToString() == "ChatCommand" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveNewRule_Click(object sender, RoutedEventArgs e)
    {
        var response = ResponseInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(response)) return;

        var item        = TriggerTypeCombo.SelectedItem as ComboBoxItem;
        var triggerTag  = item?.Tag?.ToString() ?? "ChatCommand";
        var triggerType = triggerTag switch
        {
            "NewSubscriber" => AutomationTrigger.NewSubscriber,
            "Resub"         => AutomationTrigger.Resub,
            "GiftSub"       => AutomationTrigger.GiftSub,
            "Raid"          => AutomationTrigger.Raid,
            _               => AutomationTrigger.ChatCommand
        };

        // For chat commands, require a keyword
        var keyword = CommandKeywordInput.Text.Trim();
        if (triggerType == AutomationTrigger.ChatCommand)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;
            if (!keyword.StartsWith("!")) keyword = "!" + keyword;
        }

        var (displayTrigger, displayAction, category) = triggerType switch
        {
            AutomationTrigger.NewSubscriber => ("New subscriber",             "Send thank-you message",    "Subscribers"),
            AutomationTrigger.Resub         => ("Re-subscription",            "Send welcome-back message", "Subscribers"),
            AutomationTrigger.GiftSub       => ("Gift sub",                   "Thank the gifter",          "Subscribers"),
            AutomationTrigger.Raid          => ("Raid received",              "Welcome raiders",           "Raids"),
            _                               => ($"{keyword} in chat",         "Post response",             "Commands")
        };

        var rule = new AutomationRule
        {
            Category         = category,
            TriggerType      = triggerType,
            CommandKeyword   = keyword,
            Trigger          = displayTrigger,
            Action           = displayAction,
            ResponseTemplate = response,
            IsEnabled        = true
        };

        _rules.Add(rule);
        rule.PropertyChanged += (_, _) => OnRuleToggled(rule);

        NewRuleForm.Visibility = Visibility.Collapsed;
        SaveRules();
        RefreshView();
    }

    // ── Upgrade banner ────────────────────────────────────────────────────────

    private void UpgradeBanner_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }
}
