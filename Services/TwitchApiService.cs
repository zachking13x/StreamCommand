using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace StreamCommand.Services;

public record TwitchUserInfo(string Id, string Login, string DisplayName, int FollowerCount);
public record TwitchStreamInfo(int ViewerCount, string GameName, string Title, bool IsLive);
public record TwitchChannelStats(int FollowerCount, int ViewerCount, string GameName, string Title, bool IsLive);

/// <summary>
/// Thin wrapper around the Twitch Helix REST API.
/// Requires Client-Id and a valid user access token (the TMI chat token works for public endpoints).
/// </summary>
public static class TwitchApiService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static void AddHeaders(HttpRequestMessage req, string clientId, string token)
    {
        var bearer = token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
            ? token[6..]   // strip "oauth:" prefix
            : token;
        req.Headers.TryAddWithoutValidation("Client-Id", clientId);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
    }

    /// <summary>Fetch user id and basic info. Returns null on failure.</summary>
    public static async Task<TwitchUserInfo?> GetUserAsync(string login, string clientId, string token)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(login)}");
            AddHeaders(req, clientId, token);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            var user = json?["data"]?[0];
            if (user is null) return null;

            var id = user["id"]!.GetValue<string>();

            // Fetch follower count separately (requires moderator:read:followers scope)
            int followers = await GetFollowerCountAsync(id, clientId, token);

            return new TwitchUserInfo(
                Id:           id,
                Login:        user["login"]!.GetValue<string>(),
                DisplayName:  user["display_name"]!.GetValue<string>(),
                FollowerCount: followers);
        }
        catch { return null; }
    }

    private static async Task<int> GetFollowerCountAsync(string broadcasterId, string clientId, string token)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twitch.tv/helix/channels/followers?broadcaster_id={broadcasterId}");
            AddHeaders(req, clientId, token);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return -1;
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            return json?["total"]?.GetValue<int>() ?? -1;
        }
        catch { return -1; }
    }

    /// <summary>Returns live stream info, or IsLive=false if the channel is offline.</summary>
    public static async Task<TwitchStreamInfo> GetStreamAsync(string login, string clientId, string token)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twitch.tv/helix/streams?user_login={Uri.EscapeDataString(login)}");
            AddHeaders(req, clientId, token);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new(0, "", "", false);

            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            var stream = json?["data"]?[0];
            if (stream is null) return new(0, "", "", false);   // offline

            return new TwitchStreamInfo(
                ViewerCount: stream["viewer_count"]!.GetValue<int>(),
                GameName:    stream["game_name"]?.GetValue<string>() ?? "",
                Title:       stream["title"]?.GetValue<string>() ?? "",
                IsLive:      true);
        }
        catch { return new(0, "", "", false); }
    }

    /// <summary>Convenience method — returns combined stats in one call.</summary>
    public static async Task<TwitchChannelStats?> GetChannelStatsAsync(string login, string clientId, string token)
    {
        var userTask   = GetUserAsync(login, clientId, token);
        var streamTask = GetStreamAsync(login, clientId, token);
        await Task.WhenAll(userTask, streamTask);

        var user   = await userTask;
        var stream = await streamTask;
        if (user is null) return null;

        return new TwitchChannelStats(
            FollowerCount: user.FollowerCount,
            ViewerCount:   stream.ViewerCount,
            GameName:      stream.GameName,
            Title:         stream.Title,
            IsLive:        stream.IsLive);
    }
}
