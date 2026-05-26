using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StreamCommand.Services;

public record TwitchOAuthResult(string AccessToken, string RefreshToken, string ClientId, string Username);
public record TokenValidation(bool IsValid, int ExpiresInSeconds, string Login);

/// <summary>Describes exactly which step of the auth flow failed and why.</summary>
public record TwitchAuthFailure(string Step, string Detail);

/// <summary>
/// Twitch OAuth 2.0 — Device Authorization Grant (RFC 8628).
///
/// Why Device Code instead of PKCE?
///   PKCE (authorization code) requires the Twitch app to be registered as a "Public"
///   client in the developer console, or Twitch rejects the token exchange with
///   "Invalid client credentials." Device Code flow requires only the Client ID — no
///   client secret, no redirect URI, no local HTTP listener — and works for both public
///   and confidential Twitch app types. It issues a refresh token just like PKCE.
///
/// Flow:
///   1. POST /oauth2/device → get user_code + device_code
///   2. Open browser to twitch.tv/activate, show user_code in UI
///   3. Poll /oauth2/token every 5 s until user approves (or times out / cancels)
///   4. On approval: receive access_token + refresh_token, fetch username
///
/// The Client ID is intentionally public — it appears in the device request and in
/// every URL shown to the user. This is by design for public desktop clients.
/// </summary>
public static class TwitchOAuthService
{
    public const string ClientId = "mtnw6mhjoibv7h9yxzxekkrax4s9mt";

    private static readonly string[] Scopes =
    {
        "chat:read",
        "chat:edit",
        "channel:read:subscriptions",
        "moderator:read:followers",
        "channel:read:redemptions",
    };

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // ── Authorization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Failure reason from the most recent <see cref="AuthorizeAsync"/> call.
    /// Null when the last call succeeded.
    /// </summary>
    public static TwitchAuthFailure? LastFailure { get; private set; }

    /// <summary>
    /// Runs the Device Code OAuth flow.
    ///
    /// <paramref name="onCodeReady"/> fires as soon as Twitch issues the device code.
    /// The callback receives (userCode, verificationUri) so the UI can display the
    /// short code while this method continues polling in the background.
    ///
    /// Returns null on failure; inspect <see cref="LastFailure"/> for the reason.
    /// </summary>
    public static async Task<TwitchOAuthResult?> AuthorizeAsync(
        Action<string, string>? onCodeReady = null,
        CancellationToken cancellationToken = default)
    {
        LastFailure = null;

        // ── Step 1: request a device code ────────────────────────────────────
        var session = await RequestDeviceCodeAsync();
        if (session == null) return null;   // LastFailure set inside

        // ── Step 2: hand the code to the UI, open browser ────────────────────
        onCodeReady?.Invoke(session.UserCode, session.VerificationUri);
        AppLaunchService.OpenUrl(session.VerificationUri);

        // ── Step 3: poll until approved, expired, or cancelled ───────────────
        return await PollForTokenAsync(session, cancellationToken);
    }

    // ── Device code request ───────────────────────────────────────────────────

    private record DeviceSession(
        string DeviceCode,
        string UserCode,
        string VerificationUri,
        int    ExpiresInSeconds,
        int    IntervalSeconds);

    private static async Task<DeviceSession?> RequestDeviceCodeAsync()
    {
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scopes"]    = string.Join(" ", Scopes),
            });

            var resp = await _http.PostAsync("https://id.twitch.tv/oauth2/device", form);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                LastFailure = new TwitchAuthFailure("DeviceCode",
                    $"Twitch rejected device code request (HTTP {(int)resp.StatusCode}): {Truncate(json)}");
                return null;
            }

            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new DeviceSession(
                DeviceCode:       root.GetProperty("device_code").GetString()!,
                UserCode:         root.GetProperty("user_code").GetString()!,
                VerificationUri:  root.TryGetProperty("verification_uri", out var vu)
                                      ? vu.GetString()!
                                      : "https://www.twitch.tv/activate",
                ExpiresInSeconds: root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 1800,
                IntervalSeconds:  root.TryGetProperty("interval",   out var iv) ? iv.GetInt32() : 5);
        }
        catch (Exception ex)
        {
            LastFailure = new TwitchAuthFailure("DeviceCode", $"Network error: {ex.Message}");
            return null;
        }
    }

    // ── Polling ───────────────────────────────────────────────────────────────

    private static async Task<TwitchOAuthResult?> PollForTokenAsync(
        DeviceSession session, CancellationToken externalCt)
    {
        // Honour both external cancellation and the device code expiry window
        using var expiryCts = new CancellationTokenSource(TimeSpan.FromSeconds(session.ExpiresInSeconds));
        using var linked    = CancellationTokenSource.CreateLinkedTokenSource(externalCt, expiryCts.Token);
        var ct = linked.Token;

        // Twitch says don't poll faster than interval; add 1 s buffer
        var interval = TimeSpan.FromSeconds(Math.Max(session.IntervalSeconds, 5) + 1);

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }

            try
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"]   = ClientId,
                    ["device_code"] = session.DeviceCode,
                    ["grant_type"]  = "urn:ietf:params:oauth:grant-type:device_code",
                });

                var resp = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form);
                var json = await resp.Content.ReadAsStringAsync();

                // Still waiting for the user to click Authorize
                if (!resp.IsSuccessStatusCode)
                {
                    // "authorization_pending" is the normal "not yet" response — keep looping
                    if (json.Contains("authorization_pending") || json.Contains("slow_down"))
                        continue;

                    // Any other error (e.g. "expired_token", "access_denied") is terminal
                    LastFailure = new TwitchAuthFailure("Poll",
                        $"Twitch error (HTTP {(int)resp.StatusCode}): {Truncate(json)}");
                    return null;
                }

                // Success — parse tokens
                using var doc  = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("access_token", out var at) ||
                    string.IsNullOrEmpty(at.GetString()))
                {
                    LastFailure = new TwitchAuthFailure("Poll",
                        $"Token response missing access_token: {Truncate(json)}");
                    return null;
                }

                var access  = at.GetString()!;
                var refresh = root.TryGetProperty("refresh_token", out var rt)
                                  ? rt.GetString() ?? "" : "";

                // Fetch the Twitch username to complete the result
                var username = await FetchUsernameAsync(access);
                if (string.IsNullOrEmpty(username))
                {
                    LastFailure = new TwitchAuthFailure("UserFetch",
                        "Tokens received but could not retrieve Twitch username.");
                    return null;
                }

                return new TwitchOAuthResult(access, refresh, ClientId, username);
            }
            catch (OperationCanceledException) { break; }
            catch { /* transient network error — try again next interval */ }
        }

        if (externalCt.IsCancellationRequested)
            LastFailure = new TwitchAuthFailure("Cancelled", "Connection cancelled.");
        else
            LastFailure = new TwitchAuthFailure("Timeout",
                "Device code expired — the code was not approved within the time limit.");

        return null;
    }

    // ── Token refresh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Exchanges a refresh token for a new access + refresh token pair.
    /// Returns null if the refresh token is expired or invalid (user must re-authorise).
    /// </summary>
    public static async Task<(string access, string refresh)?> RefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return null;
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = ClientId,
                ["grant_type"]    = "refresh_token",
                ["refresh_token"] = refreshToken,
            });

            var resp = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var access     = root.GetProperty("access_token").GetString() ?? "";
            var newRefresh = root.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString() ?? refreshToken
                : refreshToken;
            return string.IsNullOrEmpty(access) ? null : (access, newRefresh);
        }
        catch { return null; }
    }

    // ── Token validation ──────────────────────────────────────────────────────

    /// <summary>
    /// Validates the current access token against the Twitch /oauth2/validate endpoint.
    /// Returns (IsValid=false) if the token is expired or missing.
    /// </summary>
    public static async Task<TokenValidation> ValidateTokenAsync(string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
            return new TokenValidation(false, 0, "");
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            req.Headers.Add("Authorization", $"OAuth {accessToken}");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new TokenValidation(false, 0, "");

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var exp  = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 0;
            var lg   = root.TryGetProperty("login",      out var l)  ? l.GetString() ?? "" : "";
            return new TokenValidation(true, exp, lg);
        }
        catch { return new TokenValidation(false, 0, ""); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string?> FetchUsernameAsync(string accessToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            req.Headers.Add("Authorization", $"Bearer {accessToken}");
            req.Headers.Add("Client-Id", ClientId);
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("data")[0].GetProperty("login").GetString();
        }
        catch { return null; }
    }

    private static string Truncate(string s, int max = 250)
        => s.Length <= max ? s : s[..max] + "…";
}
