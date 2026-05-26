using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamCommand.Services;

/// <summary>
/// Connects to the Twitch EventSub WebSocket API to receive channel.follow (v2) events
/// in real time without polling. Follow events do NOT arrive via IRC — this is the only way.
///
/// Requirements:
///   • TwitchClientId and TwitchChatToken with the <c>moderator:read:followers</c> OAuth scope.
///   • The authenticated user must be the broadcaster or a moderator of the channel.
///
/// Usage:
///   1. <see cref="Instance"/> is the app-wide singleton.
///   2. Subscribe to <see cref="FollowReceived"/> before calling <see cref="ConnectAsync"/>.
///   3. Call <see cref="ConnectAsync"/> with the broadcaster's credentials at app start
///      (only if credentials are present — it fails gracefully if they are not).
/// </summary>
public sealed class EventSubService
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static readonly EventSubService Instance = new();
    private EventSubService() { }

    // ── Events ─────────────────────────────────────────────────────────────────
    /// <summary>Raised on the thread-pool when a new follower is detected. Arg = follower's user_name.</summary>
    public event Action<string>? FollowReceived;

    // ── State ─────────────────────────────────────────────────────────────────
    private ClientWebSocket?         _ws;
    private CancellationTokenSource? _cts;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    // ── Connect / Disconnect ──────────────────────────────────────────────────

    /// <summary>
    /// Connects to Twitch EventSub and subscribes to channel.follow.
    /// Resolves the broadcaster's user ID from <paramref name="username"/> via the Helix API.
    /// Fails silently (logs nothing, no crash) if credentials are missing or invalid.
    /// </summary>
    public async Task ConnectAsync(string username, string clientId, string token)
    {
        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(clientId)  ||
            string.IsNullOrWhiteSpace(token)) return;

        await DisconnectAsync();

        // Resolve broadcaster user ID — needed for the EventSub subscription
        var userInfo = await TwitchApiService.GetUserAsync(username, clientId, token);
        if (userInfo == null) return;   // bad credentials or network error

        _cts = new CancellationTokenSource();
        _ws  = new ClientWebSocket();

        try
        {
            await _ws.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws"), _cts.Token);
            _ = Task.Run(() => ListenLoopAsync(userInfo.Id, clientId, token), _cts.Token);
        }
        catch
        {
            // Network error — EventSub is optional, so don't crash or alert the user.
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        _ws?.Dispose();
        _ws  = null;
        _cts?.Dispose();
        _cts = null;
    }

    // ── Listen loop ───────────────────────────────────────────────────────────

    private async Task ListenLoopAsync(string broadcasterId, string clientId, string token)
    {
        var buf = new byte[65536];
        var sb  = new StringBuilder();

        try
        {
            while (_ws?.State == WebSocketState.Open && !(_cts?.IsCancellationRequested ?? true))
            {
                var result = await _ws.ReceiveAsync(buf, _cts!.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                if (!result.EndOfMessage) continue;

                await HandleMessageAsync(sb.ToString(), broadcasterId, clientId, token);
                sb.Clear();
            }
        }
        catch (OperationCanceledException) { }
        catch { /* socket dropped — EventSub is best-effort */ }
    }

    private async Task HandleMessageAsync(string raw, string broadcasterId, string clientId, string token)
    {
        try
        {
            var json    = JsonNode.Parse(raw);
            var msgType = json?["metadata"]?["message_type"]?.GetValue<string>();

            switch (msgType)
            {
                case "session_welcome":
                    // Exchange the session_id for an EventSub subscription
                    var sessionId = json?["payload"]?["session"]?["id"]?.GetValue<string>();
                    if (sessionId != null)
                        await SubscribeToFollowsAsync(broadcasterId, clientId, token, sessionId);
                    break;

                case "notification":
                    // Follow event payload: payload.event.user_name
                    var subType  = json?["payload"]?["subscription"]?["type"]?.GetValue<string>();
                    if (subType != "channel.follow") break;

                    var userName = json?["payload"]?["event"]?["user_name"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(userName))
                        FollowReceived?.Invoke(userName);
                    break;

                case "session_keepalive":
                    break;  // nothing to do — connection is healthy

                case "session_reconnect":
                    // Twitch is asking us to reconnect to a new URL
                    var reconnectUrl = json?["payload"]?["session"]?["reconnect_url"]?.GetValue<string>();
                    if (reconnectUrl != null)
                        await ReconnectAsync(reconnectUrl, broadcasterId, clientId, token);
                    break;
            }
        }
        catch { /* malformed payload — skip */ }
    }

    private async Task ReconnectAsync(string reconnectUrl, string broadcasterId, string clientId, string token)
    {
        var oldWs  = _ws;
        var oldCts = _cts;   // capture before overwrite
        var newCts = new CancellationTokenSource();
        var newWs  = new ClientWebSocket();

        try
        {
            await newWs.ConnectAsync(new Uri(reconnectUrl), newCts.Token);
            _ws  = newWs;
            _cts = newCts;
            _ = Task.Run(() => ListenLoopAsync(broadcasterId, clientId, token), newCts.Token);

            // Gracefully close and dispose the old socket/token after the new one is live
            if (oldWs?.State == WebSocketState.Open)
                try { await oldWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            oldWs?.Dispose();
            try { oldCts?.Cancel(); oldCts?.Dispose(); } catch { }
        }
        catch { /* reconnect failed — keep old socket running */ }
    }

    // ── Subscription request ──────────────────────────────────────────────────

    private async Task SubscribeToFollowsAsync(string broadcasterId, string clientId, string token, string sessionId)
    {
        try
        {
            var bearer = token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
                ? token[6..]
                : token;

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://api.twitch.tv/helix/eventsub/subscriptions");
            req.Headers.TryAddWithoutValidation("Client-Id", clientId);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");

            // channel.follow v2 requires broadcaster_user_id AND moderator_user_id.
            // For a streamer connecting with their own token, both IDs are the broadcaster.
            var body = new
            {
                type      = "channel.follow",
                version   = "2",
                condition = new { broadcaster_user_id = broadcasterId, moderator_user_id = broadcasterId },
                transport = new { method = "websocket", session_id = sessionId }
            };
            req.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            await _http.SendAsync(req);
            // 202 Accepted = success, 409 Conflict = already subscribed — both are fine.
            // 403 = missing scope (moderator:read:followers) — follow events won't arrive,
            //       but the rest of the app is unaffected.
        }
        catch { /* subscription failed — not fatal */ }
    }
}
