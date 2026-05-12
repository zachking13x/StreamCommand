using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StreamCommand.Models;

namespace StreamCommand.Services;

/// <summary>
/// Evaluates enabled <see cref="AutomationRule"/>s against incoming Twitch events and
/// fires the configured response through <see cref="TwitchChatService.Shared"/>.
///
/// Lifecycle:
///   1. <see cref="Instance"/> is created at app start (singleton).
///   2. Call <see cref="ReloadFromSettings"/> once at startup and again whenever the
///      user saves a rule change.
///   3. The engine listens to <see cref="TwitchChatService.Shared.MessageReceived"/>
///      (for IRC events) and <see cref="EventSubService.Instance.FollowReceived"/>
///      (for follows via EventSub) automatically — no further wiring needed.
/// </summary>
public sealed class AutomationEngine
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static readonly AutomationEngine Instance = new();

    private AutomationEngine()
    {
        TwitchChatService.Shared.MessageReceived += OnMessage;
        EventSubService.Instance.FollowReceived  += OnFollowReceived;
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private List<AutomationRule> _rules   = new();
    private string               _channel = "";

    /// <summary>
    /// Reload the active rule set from persisted settings.
    /// Call this after any rule change (toggle, add, delete, edit).
    /// </summary>
    public void ReloadFromSettings()
    {
        var s    = SettingsService.Load();
        _channel = s.TwitchUsername;

        // Merge: keep default seed rules that are missing from the saved list
        // (so a fresh install always has the defaults even if the JSON predates them)
        var saved     = s.AutomationRules;
        var savedIds  = new HashSet<string>(saved.Select(r => r.Id));
        var defaults  = new AppSettings().AutomationRules;

        foreach (var def in defaults)
            if (!savedIds.Contains(def.Id))
                saved.Add(def);

        _rules = saved.Where(r => r.IsEnabled).ToList();
    }

    // ── IRC event handler ─────────────────────────────────────────────────────

    private async void OnMessage(TwitchChatMessage msg)
    {
        if (string.IsNullOrWhiteSpace(_channel)) return;

        foreach (var rule in _rules)
        {
            if (!MatchesIrc(rule, msg)) continue;

            var response = ExpandTemplate(rule.ResponseTemplate, msg);
            if (!string.IsNullOrWhiteSpace(response))
                await TwitchChatService.Shared.SendMessageAsync(_channel, response);

            break;   // only the first matching rule fires per event
        }
    }

    // ── EventSub follow handler ───────────────────────────────────────────────

    private async void OnFollowReceived(string userName)
    {
        if (string.IsNullOrWhiteSpace(_channel)) return;

        // Build a synthetic message so ExpandTemplate can use @user
        var followMsg = new TwitchChatMessage
        {
            Username  = userName,
            EventType = TwitchEventType.Follow,
            Text      = $"🌟 {userName} just followed!",
            IsAlert   = true,
            Time      = DateTime.Now
        };

        // Raise a visible alert in the chat monitor / overlay
        StreamEvents.RaiseAlert("🌟", followMsg.Text);

        foreach (var rule in _rules)
        {
            if (rule.TriggerType != AutomationTrigger.NewFollower) continue;

            var response = ExpandTemplate(rule.ResponseTemplate, followMsg);
            if (!string.IsNullOrWhiteSpace(response))
                await TwitchChatService.Shared.SendMessageAsync(_channel, response);

            break;
        }
    }

    // ── Trigger matching (IRC events only) ───────────────────────────────────

    private static bool MatchesIrc(AutomationRule rule, TwitchChatMessage msg)
    {
        return rule.TriggerType switch
        {
            AutomationTrigger.NewSubscriber => msg.EventType == TwitchEventType.Subscribe,
            AutomationTrigger.Resub         => msg.EventType == TwitchEventType.Resub,
            AutomationTrigger.GiftSub       => msg.EventType == TwitchEventType.GiftSub,
            AutomationTrigger.Raid          => msg.EventType == TwitchEventType.Raid,

            AutomationTrigger.ChatCommand   =>
                !msg.IsAlert &&
                !string.IsNullOrWhiteSpace(rule.CommandKeyword) &&
                msg.Text.Trim().StartsWith(rule.CommandKeyword, StringComparison.OrdinalIgnoreCase),

            // NewFollower is handled separately via EventSub — never match it through IRC
            _ => false
        };
    }

    // ── Template expansion ────────────────────────────────────────────────────

    private static string ExpandTemplate(string template, TwitchChatMessage msg)
    {
        return template
            .Replace("@user",    $"@{msg.Username}",   StringComparison.OrdinalIgnoreCase)
            .Replace("@raider",  $"@{(string.IsNullOrEmpty(msg.RaiderName) ? msg.Username : msg.RaiderName)}",
                                 StringComparison.OrdinalIgnoreCase)
            .Replace("{months}",  msg.Months,           StringComparison.OrdinalIgnoreCase)
            .Replace("{viewers}", msg.ViewerCount,      StringComparison.OrdinalIgnoreCase);
    }
}
