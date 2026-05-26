using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StreamCommand.Services;

public record TwitchOAuthResult(string AccessToken, string RefreshToken, string ClientId, string Username);
public record TokenValidation(bool IsValid, int ExpiresInSeconds, string Login);

/// <summary>
/// Twitch OAuth 2.0 with PKCE (RFC 7636).
///
/// Why PKCE and not Implicit?
///   Implicit grant does not issue refresh tokens — users must re-authorise every ~60 days.
///   PKCE is the current standard for public desktop apps: no client_secret is embedded
///   or required, and the flow issues a refresh token that auto-renews access silently.
///
/// The Client ID is intentionally public — it appears in every OAuth redirect URL and in
/// the compiled binary. This is by design for public desktop clients with no server-side
/// component. See RFC 7636: https://www.rfc-editor.org/rfc/rfc7636
/// </summary>
public static class TwitchOAuthService
{
    public const  string ClientId    = "mtnw6mhjoibv7h9yxzxekkrax4s9mt";
    private const int    ListenPort  = 47821;
    private const string RedirectUri = "http://localhost:47821/callback";

    private static readonly string[] Scopes =
    {
        "chat:read",
        "chat:edit",
        "channel:read:subscriptions",
        "moderator:read:followers",
        "channel:read:redemptions",
    };

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ── Authorization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Opens Twitch OAuth in the browser, receives the authorization code via local
    /// redirect, exchanges it for access + refresh tokens (PKCE), and returns the result.
    /// Returns null if the user cancelled, timed out, or an error occurred.
    /// </summary>
    public static async Task<TwitchOAuthResult?> AuthorizeAsync()
    {
        var state        = Guid.NewGuid().ToString("N");
        var verifier     = GenerateCodeVerifier();
        var challenge    = GenerateCodeChallenge(verifier);
        var scope        = Uri.EscapeDataString(string.Join(" ", Scopes));

        var authUrl = "https://id.twitch.tv/oauth2/authorize"
                    + $"?client_id={ClientId}"
                    + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                    + $"&response_type=code"
                    + $"&scope={scope}"
                    + $"&state={Uri.EscapeDataString(state)}"
                    + $"&code_challenge={challenge}"
                    + $"&code_challenge_method=S256";

        AppLaunchService.OpenUrl(authUrl);

        // ── Receive the authorization code ────────────────────────────────────
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{ListenPort}/");
        listener.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        string? code          = null;
        string? returnedState = null;

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var ctx  = await listener.GetContextAsync().WaitAsync(cts.Token);
                var req  = ctx.Request;
                var resp = ctx.Response;

                if (req.Url?.AbsolutePath == "/callback")
                {
                    code          = req.QueryString["code"];
                    returnedState = req.QueryString["state"];
                    var error     = req.QueryString["error"];

                    // Serve a friendly page so the user can close the browser tab
                    var html  = string.IsNullOrEmpty(error) ? SuccessHtml() : ErrorHtml(error ?? "unknown_error");
                    var bytes = Encoding.UTF8.GetBytes(html);
                    resp.ContentType     = "text/html; charset=utf-8";
                    resp.ContentLength64 = bytes.Length;
                    await resp.OutputStream.WriteAsync(bytes, cts.Token);
                    resp.Close();
                    break;
                }

                resp.StatusCode = 404;
                resp.Close();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { listener.Stop(); } catch { }
        }

        if (string.IsNullOrEmpty(code) || returnedState != state)
            return null;

        // ── Exchange code for tokens ──────────────────────────────────────────
        var tokens = await ExchangeCodeAsync(code, verifier);
        if (tokens == null) return null;

        var username = await FetchUsernameAsync(tokens.Value.access);
        if (string.IsNullOrEmpty(username)) return null;

        return new TwitchOAuthResult(tokens.Value.access, tokens.Value.refresh, ClientId, username);
    }

    // ── Token exchange ────────────────────────────────────────────────────────

    private static async Task<(string access, string refresh)?> ExchangeCodeAsync(string code, string verifier)
    {
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = ClientId,
                ["code"]          = code,
                ["code_verifier"] = verifier,
                ["grant_type"]    = "authorization_code",
                ["redirect_uri"]  = RedirectUri,
            });

            var resp = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var access  = root.GetProperty("access_token").GetString() ?? "";
            var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
            return string.IsNullOrEmpty(access) ? null : (access, refresh);
        }
        catch { return null; }
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

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── Callback HTML pages ───────────────────────────────────────────────────

    private static string SuccessHtml() => """
        <!DOCTYPE html><html><head><meta charset="utf-8"/>
        <title>Stream Command — Connected</title>
        <style>body{background:#191C1E;color:#D4DDD6;font-family:system-ui,sans-serif;
        display:flex;flex-direction:column;align-items:center;justify-content:center;
        height:100vh;margin:0;gap:14px;}
        h2{color:#8ABBA6;margin:0;font-size:20px;}
        p{color:#5E6E66;font-size:14px;margin:0;}</style></head>
        <body><h2>✓  Connected to Twitch!</h2>
        <p>You can close this tab and return to Stream Command.</p></body></html>
        """;

    private static string ErrorHtml(string error) => $$"""
        <!DOCTYPE html><html><head><meta charset="utf-8"/>
        <title>Stream Command — Error</title>
        <style>body{background:#191C1E;color:#D4DDD6;font-family:system-ui,sans-serif;
        display:flex;flex-direction:column;align-items:center;justify-content:center;
        height:100vh;margin:0;gap:14px;}
        h2{color:#EF4444;margin:0;font-size:20px;}
        p{color:#5E6E66;font-size:14px;margin:0;}</style></head>
        <body><h2>⚠  Authorisation failed</h2>
        <p>{{System.Security.SecurityElement.Escape(error)}}</p>
        <p>Close this tab and try again in Stream Command.</p></body></html>
        """;
}
