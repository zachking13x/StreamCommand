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
    // The live list — bound to the UI, persisted on every change
    private ObservableCollection<AutomationRule> _rules = new();

    // Guard against re-entrant saves when we programmatically revert a toggle
    private bool _suspendSave;

    // When non-null, SaveNewRule_Click updates this rule in place instead of adding
    private AutomationRule? _editingRule;

    public AutomationView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadRules();
            // Re-evaluate Pro gate whenever entitlement is confirmed
            EntitlementService.Refreshed += () => Dispatcher.Invoke(RefreshView);
        };
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
        bool isPro = FeatureGate.Has("automation-unlimited");
        FreeLimitBanner.Visibility = !isPro ? Visibility.Visible : Visibility.Collapsed;

        // UX 3: show live count so users see the wall approaching before they hit it
        if (!isPro)
        {
            int used = _rules.Count(r => r.IsEnabled);
            FreeLimitBanner.FeatureLabel = used >= 3
                ? $"🔒  Free plan: {used}/3 rules active — upgrade Pro for unlimited"
                : $"🔒  Free plan: {used}/3 active rules — upgrade Pro for unlimited";
        }

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

        // Free tier: cap active rules at 3 — BUG 3 fix: count OTHERS to avoid off-by-one
        if (rule.IsEnabled && !FeatureGate.Has("automation-unlimited"))
        {
            // Count enabled rules excluding this one — if already 3 others are enabled,
            // enabling this one would bring the total to 4 (over limit). Revert it.
            int otherEnabled = _rules.Count(r => r.IsEnabled && r != rule);
            if (otherEnabled >= 3)
            {
                _suspendSave = true;
                rule.IsEnabled = false;
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
        if (!FeatureGate.Has("automation-unlimited") && _rules.Count(r => r.IsEnabled) >= 3)
        {
            var win = new ProUpgradeWindow { Owner = Window.GetWindow(this) };
            win.ShowDialog();
            return;
        }

        _editingRule = null;
        RuleFormTitle.Text             = "New Rule";
        TriggerTypeCombo.SelectedIndex = 0;
        CommandKeywordPanel.Visibility = Visibility.Collapsed;
        ResponseInput.Text             = "";
        CommandKeywordInput.Text       = "";
        NewRuleForm.Visibility         = Visibility.Visible;
    }

    private void CancelNewRule_Click(object sender, RoutedEventArgs e)
    {
        _editingRule           = null;
        NewRuleForm.Visibility = Visibility.Collapsed;
    }

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
            "NewFollower"   => AutomationTrigger.NewFollower,
            _               => AutomationTrigger.ChatCommand
        };

        var keyword = CommandKeywordInput.Text.Trim();
        if (triggerType == AutomationTrigger.ChatCommand)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;
            if (!keyword.StartsWith("!")) keyword = "!" + keyword;
        }

        var (displayTrigger, displayAction, category) = triggerType switch
        {
            AutomationTrigger.NewSubscriber => ("New subscriber",  "Send thank-you message",    "Subscribers"),
            AutomationTrigger.Resub         => ("Re-subscription", "Send welcome-back message", "Subscribers"),
            AutomationTrigger.GiftSub       => ("Gift sub",        "Thank the gifter",          "Subscribers"),
            AutomationTrigger.Raid          => ("Raid received",   "Welcome raiders",           "Raids"),
            AutomationTrigger.NewFollower   => ("New follower",    "Send welcome message",      "Followers"),
            _                               => ($"{keyword} in chat", "Post response",          "Commands")
        };

        if (_editingRule != null)
        {
            // Update in place — preserves the rule's Id and IsEnabled state
            _editingRule.TriggerType      = triggerType;
            _editingRule.CommandKeyword   = keyword;
            _editingRule.ResponseTemplate = response;
            _editingRule.Trigger          = displayTrigger;
            _editingRule.Action           = displayAction;
            _editingRule.Category         = category;
            _editingRule = null;
        }
        else
        {
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
        }

        NewRuleForm.Visibility = Visibility.Collapsed;
        SaveRules();
        RefreshView();
    }

    // ── Edit / Delete ─────────────────────────────────────────────────────────

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AutomationRule rule }) return;

        _editingRule   = rule;
        RuleFormTitle.Text = "Edit Rule";

        // Select the matching trigger type in the combo
        var tagStr = rule.TriggerType switch
        {
            AutomationTrigger.NewSubscriber => "NewSubscriber",
            AutomationTrigger.Resub         => "Resub",
            AutomationTrigger.GiftSub       => "GiftSub",
            AutomationTrigger.Raid          => "Raid",
            AutomationTrigger.NewFollower   => "NewFollower",
            _                               => "ChatCommand"
        };
        foreach (ComboBoxItem cbi in TriggerTypeCombo.Items)
            if (cbi.Tag?.ToString() == tagStr) { TriggerTypeCombo.SelectedItem = cbi; break; }

        CommandKeywordInput.Text = rule.CommandKeyword;
        ResponseInput.Text       = rule.ResponseTemplate;
        CommandKeywordPanel.Visibility =
            rule.TriggerType == AutomationTrigger.ChatCommand ? Visibility.Visible : Visibility.Collapsed;

        NewRuleForm.Visibility = Visibility.Visible;
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AutomationRule rule }) return;
        _rules.Remove(rule);
        SaveRules();
        RefreshView();
    }

    // ── Upgrade banner ────────────────────────────────────────────────────────

    // ProGateBanner handles upgrade clicks internally — no handler needed here
}
