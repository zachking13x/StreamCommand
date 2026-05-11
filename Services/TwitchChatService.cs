using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamCommand.Services;

public enum TwitchEventType
{
    ChatMessage,
    Subscribe,
    Resub,
    GiftSub,
    Raid,
}

public class TwitchChatMessage
{
    public string         Username      { get; set; } = "";
    public string         Color         { get; set; } = "#C084FC";
    public string         Text          { get; set; } = "";
    public bool           IsSub         { get; set; }
    public bool           IsMod         { get; set; }
    public bool           IsVip         { get; set; }
    public bool           IsAlert       { get; set; }   // sub / resub / gift / raid
    public DateTime       Time          { get; set; } = DateTime.Now;

    // Typed event — set by ParseUserNotice so AutomationEngine can match without string parsing
    public TwitchEventType EventType    { get; set; } = TwitchEventType.ChatMessage;

    // Extra context for template expansion (@raider, {months})
    public string         Months        { get; set; } = "1";
    public string         RaiderName    { get; set; } = "";
    public string         ViewerCount   { get; set; } = "0";
}

/// <summary>
/// Connects to Twitch chat via IRC over WebSocket.
/// Use <see cref="Shared"/> for the single app-wide instance so that
/// ChatMonitor, AutomationEngine, and any other subscriber share one connection.
/// </summary>
public class TwitchChatService
{
    // ── Shared singleton ─────────────────────────────────────────────────────
    /// <summary>App-wide shared instance. Wire all subscribers here.</summary>
    public static readonly TwitchChatService Shared = new();

    // ── State ────────────────────────────────────────────────────────────────
    private ClientWebSocket?         _ws;
    private CancellationTokenSource? _cts;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event Action<TwitchChatMessage>? MessageReceived;
    public event Action<string>?            StatusChanged;
    public event Action?                    Connected;

    // ── Connect ──────────────────────────────────────────────────────────────

    public async Task ConnectAsync(string channel, string username, string oauthToken)
    {
        await DisconnectAsync();

        _cts = new CancellationTokenSource();
        _ws  = new ClientWebSocket();

        StatusChanged?.Invoke("Connecting to Twitch chat…");
        try
        {
            await _ws.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), _cts.Token);

            await IrcSendAsync("CAP REQ :twitch.tv/tags twitch.tv/commands");

            var token = oauthToken.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
                ? oauthToken
                : "oauth:" + oauthToken;
            await IrcSendAsync($"PASS {token}");
            await IrcSendAsync($"NICK {username.ToLower()}");
            await IrcSendAsync($"JOIN #{channel.ToLower().TrimStart('#')}");

            StatusChanged?.Invoke($"Joining #{channel} chat…");
            _ = Task.Run(ListenLoopAsync, _cts.Token);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Failed to connect: {ex.Message}");
        }
    }

    /// <summary>Sends a chat message to the joined channel as the authenticated user.</summary>
    public async Task SendMessageAsync(string channel, string message)
    {
        if (!IsConnected) return;
        await IrcSendAsync($"PRIVMSG #{channel.ToLower().TrimStart('#')} :{message}");
    }

    public async Task DisconnectAsync()
    {
        _joinConfirmed = false;
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        _ws?.Dispose();
        _ws  = null;
        _cts?.Dispose();
        _cts = null;
    }

    // ── Listen loop ──────────────────────────────────────────────────────────

    private async Task ListenLoopAsync()
    {
        var buf = new byte[8192];
        var sb  = new StringBuilder();

        try
        {
            while (_ws?.State == WebSocketState.Open && !(_cts?.IsCancellationRequested ?? true))
            {
                var result = await _ws.ReceiveAsync(buf, _cts!.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                if (!result.EndOfMessage) continue;

                foreach (var line in sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    await HandleLineAsync(line.Trim());

                sb.Clear();
            }
        }
        catch (OperationCanceledException) { }
        catch { /* socket closed */ }

        StatusChanged?.Invoke("Disconnected from chat");
    }

    private bool _joinConfirmed;

    private async Task HandleLineAsync(string line)
    {
        if (line.StartsWith("PING"))
        {
            await IrcSendAsync("PONG :tmi.twitch.tv");
            return;
        }

        if (!_joinConfirmed && (line.Contains(" 376 ") || line.Contains(" 366 ") || line.Contains("JOIN #")))
        {
            _joinConfirmed = true;
            StatusChanged?.Invoke("Connected — reading live chat");
            Connected?.Invoke();
        }

        if (line.Contains("Login authentication failed") || line.Contains("NOTICE * :"))
        {
            StatusChanged?.Invoke("⚠  Login failed — check your Twitch token in Settings");
            return;
        }

        string tags      = "";
        string remainder = line;

        if (line.StartsWith("@"))
        {
            var sp = line.IndexOf(' ');
            if (sp < 0) return;
            tags      = line[1..sp];
            remainder = line[(sp + 1)..];
        }

        if (remainder.Contains("PRIVMSG"))
        {
            var msg = ParsePrivmsg(tags, remainder);
            if (msg is not null) MessageReceived?.Invoke(msg);
            return;
        }

        if (remainder.Contains("USERNOTICE"))
        {
            var msg = ParseUserNotice(tags, remainder);
            if (msg is not null) MessageReceived?.Invoke(msg);
        }
    }

    // ── Parsers ──────────────────────────────────────────────────────────────

    private static TwitchChatMessage? ParsePrivmsg(string tags, string remainder)
    {
        var prefixEnd = remainder.IndexOf(' ');
        if (prefixEnd < 0) return null;
        var prefix   = remainder[1..prefixEnd];
        var excl     = prefix.IndexOf('!');
        var username = excl > 0 ? prefix[..excl] : prefix;

        var msgStart = remainder.LastIndexOf(" :");
        if (msgStart < 0) return null;
        var text = remainder[(msgStart + 2)..].Trim();

        var t = ParseTags(tags);
        return new TwitchChatMessage
        {
            Username  = username,
            Color     = t.color,
            Text      = text,
            IsSub     = t.isSub,
            IsMod     = t.isMod,
            IsVip     = t.isVip,
            EventType = TwitchEventType.ChatMessage,
            Time      = DateTime.Now
        };
    }

    private static TwitchChatMessage? ParseUserNotice(string tags, string remainder)
    {
        var t = ParseTags(tags);

        // Map msg-id → EventType and human-readable text
        TwitchEventType eventType;
        string alertText;

        switch (t.msgId)
        {
            case "sub":
                eventType = TwitchEventType.Subscribe;
                alertText = $"⭐ {t.displayName} just subscribed!";
                break;
            case "resub":
                eventType = TwitchEventType.Resub;
                alertText = $"⭐ {t.displayName} resubscribed for {t.months} months!";
                break;
            case "subgift":
            case "submysterygift":
                eventType = TwitchEventType.GiftSub;
                alertText = t.msgId == "submysterygift"
                    ? $"🎁 {t.displayName} is gifting {t.giftCount} subs!"
                    : $"🎁 {t.displayName} gifted a sub to {t.recipient}!";
                break;
            case "raid":
                eventType = TwitchEventType.Raid;
                alertText = $"🚀 {t.displayName} is raiding with {t.viewerCount} viewers!";
                break;
            default:
                return null;
        }

        return new TwitchChatMessage
        {
            Username    = t.displayName,
            Color       = "#F59E0B",
            Text        = alertText,
            IsAlert     = true,
            EventType   = eventType,
            Months      = t.months,
            RaiderName  = t.displayName,
            ViewerCount = t.viewerCount,
            Time        = DateTime.Now
        };
    }

    private static (string color, bool isSub, bool isMod, bool isVip, string msgId,
                    string displayName, string months, string recipient, string giftCount, string viewerCount)
        ParseTags(string tags)
    {
        string color = "#C084FC", msgId = "", displayName = "", months = "1",
               recipient = "", giftCount = "1", viewerCount = "0";
        bool isSub = false, isMod = false, isVip = false;

        foreach (var tag in tags.Split(';'))
        {
            var eq  = tag.IndexOf('=');
            if (eq < 0) continue;
            var key = tag[..eq];
            var val = tag[(eq + 1)..];

            switch (key)
            {
                case "color"              when !string.IsNullOrEmpty(val): color       = val;   break;
                case "subscriber":        isSub        = val == "1"; break;
                case "mod":               isMod        = val == "1"; break;
                case "vip":               isVip        = val == "1"; break;
                case "msg-id":            msgId        = val;        break;
                case "display-name":      displayName  = val;        break;
                case "msg-param-months":  months       = val;        break;
                case "msg-param-recipient-display-name": recipient   = val; break;
                case "msg-param-mass-gift-count":        giftCount   = val; break;
                case "msg-param-viewerCount":            viewerCount = val; break;
            }
        }

        return (color, isSub, isMod, isVip, msgId, displayName, months, recipient, giftCount, viewerCount);
    }

    // ── IRC send helper ──────────────────────────────────────────────────────

    private async Task IrcSendAsync(string line)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
    }
}
