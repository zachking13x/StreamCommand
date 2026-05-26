using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamCommand.Models;

namespace StreamCommand.Services;

/// <summary>
/// A single scheduled stream event — stored in settings so the Planner survives restarts.
/// </summary>
public class PlannerEvent
{
    /// <summary>Stable Guid — used for reminder deduplication. Never changes on rename/edit.</summary>
    public string   Id       { get; set; } = Guid.NewGuid().ToString();
    public string   Title    { get; set; } = "";
    public string   Platform { get; set; } = "Twitch";
    public DateTime When     { get; set; } = DateTime.Now.AddDays(1);
    public string   Duration { get; set; } = "2h";
    public string   Notes    { get; set; } = "";
}

public class AppSettings
{
    public string TwitchUsername      { get; set; } = "";
    public string TwitchClientId      { get; set; } = "";
    public string TwitchClientSecret  { get; set; } = "";   // legacy field — kept for migration only
    public string TwitchChatToken     { get; set; } = "";   // OAuth access token (PKCE)
    public string TwitchRefreshToken  { get; set; } = "";   // OAuth refresh token — auto-renews access token
    public string YoutubeApiKey       { get; set; } = "";
    public string YoutubeChannelId    { get; set; } = "";
    public string StreamElementsToken { get; set; } = "";
    public string DiscordInvite       { get; set; } = "";
    public string OBSWebSocketPassword{ get; set; } = ""; // leave blank if OBS has no password set
    public int    OBSWebSocketPort    { get; set; } = 4455;
    public bool   SetupComplete       { get; set; } = false;
    public List<PlannerEvent>    PlannerEvents    { get; set; } = new();

    /// <summary>
    /// Automation rules persisted across sessions.
    /// Seeded with defaults on first run; user toggles and additions are saved here.
    /// </summary>
    public List<AutomationRule>  AutomationRules  { get; set; } = new()
    {
        new() { Id="default-sub",    Category="Subscribers", TriggerType=AutomationTrigger.NewSubscriber,
                Trigger="New subscriber",             Action="Send thank-you message",
                ResponseTemplate="Thanks for subscribing, @user! Welcome to the community! \U0001f389",
                IsEnabled=true },
        new() { Id="default-resub",  Category="Subscribers", TriggerType=AutomationTrigger.Resub,
                Trigger="Re-subscription (any tier)", Action="Send welcome-back message",
                ResponseTemplate="Welcome back @user! Month {months} and counting! \U0001f525",
                IsEnabled=true },
        new() { Id="default-gift",   Category="Subscribers", TriggerType=AutomationTrigger.GiftSub,
                Trigger="Gift sub",                   Action="Thank the gifter",
                ResponseTemplate="Massive thanks to @user for the gift sub! \U0001f381",
                IsEnabled=true },
        new() { Id="default-follow",  Category="Followers",   TriggerType=AutomationTrigger.NewFollower,
                Trigger="New follower",               Action="Send welcome message",
                ResponseTemplate="Thanks for the follow, @user! Welcome to the community! \U0001f44b",
                IsEnabled=true },
        new() { Id="default-raid",   Category="Raids",       TriggerType=AutomationTrigger.Raid,
                Trigger="Raid received",              Action="Welcome raiders message",
                ResponseTemplate="Welcome raiders from @raider! \U0001f680 Make yourselves at home!",
                IsEnabled=false },
        new() { Id="default-socials",Category="Commands",    TriggerType=AutomationTrigger.ChatCommand,
                CommandKeyword="!socials",            Trigger="!socials in chat",
                Action="Post social links",
                ResponseTemplate="Follow me on Twitter and YouTube — links in the channel description!",
                IsEnabled=true },
        new() { Id="default-sched",  Category="Commands",    TriggerType=AutomationTrigger.ChatCommand,
                CommandKeyword="!schedule",           Trigger="!schedule in chat",
                Action="Post upcoming schedule",
                ResponseTemplate="Check my Twitch schedule panel for upcoming stream times!",
                IsEnabled=true },
    };

    /// <summary>
    /// Keys of PlannerEvents that have already triggered a pre-stream reminder toast.
    /// Key = "{Title}_{When:yyyyMMddHHmm}". Prevents double-notifying across app restarts.
    /// </summary>
    public List<string> NotifiedEventIds   { get; set; } = new();

    /// <summary>
    /// Milestone labels (e.g. "1000", "5000") that have already been celebrated with a toast.
    /// </summary>
    public List<string> CelebratedMilestones { get; set; } = new();

    public List<ChatCommand>   ChatCommands  { get; set; } = new()
    {
        new() { Trigger = "!discord",  Response = "Join our Discord! Check the channel description for the link.", IsEnabled = true  },
        new() { Trigger = "!socials",  Response = "Follow me on Twitter and YouTube — links in the channel description!", IsEnabled = true  },
        new() { Trigger = "!schedule", Response = "Check my Twitch schedule panel for upcoming stream times!", IsEnabled = true  },
        new() { Trigger = "!lurk",     Response = "Thanks for the lurk! Every viewer counts 👀", IsEnabled = true  },
    };
}

public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamCommand",
        "settings.json"
    );

    // ── Write-through cache — prevents repeated disk reads on every poll/toggle ─
    private static AppSettings? _cache;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        if (_cache != null) return _cache;   // serve from cache

        try
        {
            if (!File.Exists(SettingsPath))
            {
                _cache = new AppSettings();
                return _cache;
            }

            var json     = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOpts) ?? new AppSettings();

            // Unprotect DPAPI-wrapped secrets (backward-compat: plaintext values pass through)
            settings.TwitchChatToken      = CredentialProtection.Unprotect(settings.TwitchChatToken);
            settings.TwitchRefreshToken   = CredentialProtection.Unprotect(settings.TwitchRefreshToken);
            settings.TwitchClientSecret   = CredentialProtection.Unprotect(settings.TwitchClientSecret);
            settings.YoutubeApiKey        = CredentialProtection.Unprotect(settings.YoutubeApiKey);
            settings.StreamElementsToken  = CredentialProtection.Unprotect(settings.StreamElementsToken);
            settings.OBSWebSocketPassword = CredentialProtection.Unprotect(settings.OBSWebSocketPassword);

            // Back-fill PlannerEvents that pre-date the stable-Id field
            foreach (var ev in settings.PlannerEvents)
                if (string.IsNullOrEmpty(ev.Id))
                    ev.Id = Guid.NewGuid().ToString();

            _cache = settings;
            return _cache;
        }
        catch
        {
            _cache = new AppSettings();
            return _cache;
        }
    }

    public static void Save(AppSettings settings)
    {
        _cache = settings;   // keep cache current

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        // Write a clone with DPAPI-protected secrets — never modify the live object
        var toWrite = new AppSettings
        {
            TwitchUsername       = settings.TwitchUsername,
            TwitchClientId       = settings.TwitchClientId,
            YoutubeChannelId     = settings.YoutubeChannelId,
            DiscordInvite        = settings.DiscordInvite,
            OBSWebSocketPort     = settings.OBSWebSocketPort,
            SetupComplete        = settings.SetupComplete,
            PlannerEvents        = settings.PlannerEvents,
            AutomationRules      = settings.AutomationRules,
            NotifiedEventIds     = settings.NotifiedEventIds,
            CelebratedMilestones = settings.CelebratedMilestones,
            ChatCommands         = settings.ChatCommands,

            TwitchChatToken      = CredentialProtection.Protect(settings.TwitchChatToken),
            TwitchRefreshToken   = CredentialProtection.Protect(settings.TwitchRefreshToken),
            TwitchClientSecret   = CredentialProtection.Protect(settings.TwitchClientSecret),
            YoutubeApiKey        = CredentialProtection.Protect(settings.YoutubeApiKey),
            StreamElementsToken  = CredentialProtection.Protect(settings.StreamElementsToken),
            OBSWebSocketPassword = CredentialProtection.Protect(settings.OBSWebSocketPassword),
        };

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(toWrite, _jsonOpts));
    }
}
