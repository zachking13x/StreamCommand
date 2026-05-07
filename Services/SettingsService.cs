using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StreamCommand.Services;

/// <summary>
/// A single scheduled stream event — stored in settings so the Planner survives restarts.
/// </summary>
public class PlannerEvent
{
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
    public string TwitchClientSecret  { get; set; } = "";
    public string TwitchChatToken     { get; set; } = "";   // oauth token for IRC chat — get free at twitchapps.com/tmi
    public string YoutubeApiKey       { get; set; } = "";
    public string YoutubeChannelId    { get; set; } = "";
    public string StreamElementsToken { get; set; } = "";
    public string DiscordInvite       { get; set; } = "";
    public string OBSWebSocketPassword{ get; set; } = ""; // leave blank if OBS has no password set
    public int    OBSWebSocketPort    { get; set; } = 4455;
    public bool   SetupComplete       { get; set; } = false;
    public List<PlannerEvent> PlannerEvents { get; set; } = new();
}

public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamCommand",
        "settings.json"
    );

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
