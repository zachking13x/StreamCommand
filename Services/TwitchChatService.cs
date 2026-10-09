using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StreamCommand.Services;

public enum TwitchEventType
{
    ChatMessage,
    Subscribe,
    Resub,
    GiftSub,
    Raid,
    Follow,   // EventSub channel.follow — does NOT come through IRC
}

/// <summary>Lifecycle state of the chat connection, published via <see cref="TwitchChatService.StateChanged"/>.</summary>
public enum TwitchChatState
{
    Stopped,
    Connecting,
    Connected,
    Reconnecting,
    AuthenticationFailed,
    ChannelUnavailable,
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

    /// <summary>
    /// The app's own outgoing message, surfaced locally because Twitch doesn't echo your
    /// PRIVMSG back. Automation must ignore these, or the bot answers its own replies.
    /// </summary>
    public bool           IsLocalEcho   { get; set; }
}

/// <summary>
/// Connects to Twitch chat via IRC over WebSocket.
/// Use <see cref="Shared"/> for the single app-wide instance so that
/// ChatMonitor, AutomationEngine, and any other subscriber share one connection.
///
/// Connection lifecycle (reliability review Twitch-01/02):
/// A single supervisor owns the desired state (<see cref="ShouldBeConnected"/>) and a
/// monotonically increasing generation number. Every socket, listener, and writer task
/// captures its own generation and exits without touching shared state once a newer
/// generation exists — this prevents a stale listener from cancelling a fresh connection
/// or spawning a duplicate reconnect loop.
/// </summary>
public class TwitchChatService
{
    // ── Shared singleton ─────────────────────────────────────────────────────
    /// <summary>App-wide shared instance. Wire all subscribers here.</summary>
    public static readonly TwitchChatService Shared = new();

    // ── Per-connection state (owned by one generation) ───────────────────────

    private sealed class Connection
    {
        public required int                     Generation   { get; init; }
        public required ClientWebSocket         Socket       { get; init; }
        public required CancellationTokenSource Cts          { get; init; }
        public required string                  Channel      { get; init; }
        /// <summary>Control lines (PONG, CAP/PASS/NICK/JOIN) — always sent ahead of chat.</summary>
        public Channel<string> ControlOut { get; } = System.Threading.Channels.Channel.CreateBounded<string>(64);
        /// <summary>User chat lines (PRIVMSG) — rate limited. Echo fires only once written.</summary>
        public Channel<OutboundChat> ChatOut { get; } = System.Threading.Channels.Channel.CreateBounded<OutboundChat>(64);
        /// <summary>
        /// One wake-up source for the writer, released on every enqueue to either lane.
        /// Waiting on both channels' WaitToReadAsync abandoned a waiter every idle loop.
        /// </summary>
        public SemaphoreSlim Signal { get; } = new(0);
        public volatile bool JoinConfirmed;
    }

    private sealed record OutboundChat(string Line, TwitchChatMessage Echo);

    private readonly object _gate = new();
    private Connection? _active;
    private int _generation;

    /// <summary>Desired state — true between ConnectAsync and DisconnectAsync.</summary>
    private bool _shouldBeConnected;

    /// <summary>Set when a failure is terminal (bad token, suspended channel) — stops retries.</summary>
    private bool _terminalFailure;

    // Desired connection parameters, retained for reconnect.
    private string _desiredChannel  = "";
    private string _desiredUsername = "";
    private string _desiredToken    = "";

    // Backoff ladder: 3s → 6s → 12s → 24s → 30s, then give up.
    private static readonly int[] _backoffSeconds = { 3, 6, 12, 24, 30 };
    private int _reconnectAttempts;

    // ── Public surface ───────────────────────────────────────────────────────

    /// <summary>True only when the socket is open AND the channel JOIN was confirmed.</summary>
    public bool IsConnected
    {
        get { lock (_gate) return _active is { JoinConfirmed: true } c && c.Socket.State == WebSocketState.Open; }
    }

    public TwitchChatState State { get; private set; } = TwitchChatState.Stopped;

    public event Action<TwitchChatMessage>? MessageReceived;
    public event Action<string>?            StatusChanged;
    public event Action?                    Connected;
    public event Action<TwitchChatState>?   StateChanged;

    private void SetState(TwitchChatState state, string status)
    {
        State = state;
        StatusChanged?.Invoke(status);
        StateChanged?.Invoke(state);
    }

    // ── Connect / Disconnect ─────────────────────────────────────────────────

    public async Task ConnectAsync(string channel, string username, string oauthToken)
    {
        _desiredChannel    = channel;
        _desiredUsername   = username;
        _desiredToken      = oauthToken;
        _shouldBeConnected = true;
        _terminalFailure   = false;
        _reconnectAttempts = 0;

        await OpenConnectionAsync();
    }

    /// <summary>
    /// Connects from saved settings, or does nothing if chat is already up on the same
    /// credentials. Chat used to start only when Chat Monitor was first opened, so the
    /// dashboard chat panel and every automation rule sat dead until the user happened to
    /// visit that page, and each return visit dropped and rebuilt the connection.
    /// </summary>
    public Task StartFromSettingsAsync()
    {
        var s = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(s.TwitchUsername) || string.IsNullOrWhiteSpace(s.TwitchChatToken))
            return Task.CompletedTask;

        bool active = State is TwitchChatState.Connecting or TwitchChatState.Connected or TwitchChatState.Reconnecting;
        if (active && s.TwitchChatToken == _desiredToken && s.TwitchUsername == _desiredChannel)
            return Task.CompletedTask;

        return ConnectAsync(s.TwitchUsername, s.TwitchUsername, s.TwitchChatToken);
    }

    public async Task DisconnectAsync()
    {
        _shouldBeConnected = false;      // supervisor must not retry after this
        _reconnectAttempts = 0;
        await TeardownActiveAsync();
        SetState(TwitchChatState.Stopped, "Disconnected from chat");
    }

    /// <summary>Opens a new generation, retiring any previous one.</summary>
    private async Task OpenConnectionAsync()
    {
        await TeardownActiveAsync();

        Connection conn;
        lock (_gate)
        {
            _generation++;
            conn = new Connection
            {
                Generation = _generation,
                Socket     = new ClientWebSocket(),
                Cts        = new CancellationTokenSource(),
                Channel    = _desiredChannel.ToLowerInvariant().TrimStart('#'),
            };
            _active = conn;
        }

        SetState(TwitchChatState.Connecting, "Connecting to Twitch chat…");

        try
        {
            // Bounded deadline for the TCP/TLS handshake so a black-holed socket cannot
            // hang the supervisor and stall the whole reconnect ladder.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(conn.Cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(10));
            await conn.Socket.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), connectCts.Token);

            // Start the writer before queuing anything so control lines flush immediately.
            _ = Task.Run(() => WriterLoopAsync(conn));
            _ = Task.Run(() => ListenLoopAsync(conn));

            var token = _desiredToken.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
                ? _desiredToken
                : "oauth:" + _desiredToken;

            EnqueueControl(conn, "CAP REQ :twitch.tv/tags twitch.tv/commands");
            EnqueueControl(conn, $"PASS {token}");
            EnqueueControl(conn, $"NICK {_desiredUsername.ToLowerInvariant()}");
            EnqueueControl(conn, $"JOIN #{conn.Channel}");

            StatusChanged?.Invoke($"Joining #{conn.Channel} chat…");
            // The backoff ladder resets on confirmed JOIN (HandleLine), never on socket-open —
            // a bad token opens the socket then drops, which must NOT reset it.
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Failed to connect: {ex.Message}");
            ScheduleReconnect(conn.Generation);
        }
    }

    /// <summary>Closes and disposes the current connection without changing desired state.</summary>
    private async Task TeardownActiveAsync()
    {
        Connection? old;
        lock (_gate)
        {
            old = _active;
            _active = null;
        }
        if (old == null) return;

        old.ControlOut.Writer.TryComplete();
        old.ChatOut.Writer.TryComplete();
        try { old.Cts.Cancel(); } catch { }

        // Lines still queued were never written. Say so, since the user never saw them echo.
        int unsent = 0;
        while (old.ChatOut.Reader.TryRead(out _)) unsent++;
        if (unsent > 0) StatusChanged?.Invoke($"{unsent} chat message(s) not sent — connection closed.");

        if (old.Socket.State == WebSocketState.Open)
        {
            try { await old.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
            catch { }
        }
        try { old.Socket.Dispose(); } catch { }
        try { old.Cts.Dispose(); }    catch { }
    }

    /// <summary>True when <paramref name="generation"/> is still the live connection.</summary>
    private bool IsCurrent(int generation)
    {
        lock (_gate) return _active?.Generation == generation;
    }

    // ── Outbound: single writer per connection, control prioritized ───────────

    private static void EnqueueControl(Connection conn, string line)
    {
        if (conn.ControlOut.Writer.TryWrite(line)) conn.Signal.Release();
    }

    private readonly ChatRateLimiter _rateLimiter = new();

    // Upper bound on a throttled-chat nap, so a PONG queued meanwhile waits at most this long.
    private static readonly TimeSpan ThrottlePoll = TimeSpan.FromMilliseconds(250);

    private async Task WriterLoopAsync(Connection conn)
    {
        var ct = conn.Cts.Token;
        try
        {
            while (!ct.IsCancellationRequested && IsCurrent(conn.Generation))
            {
                if (conn.Socket.State != WebSocketState.Open) break;

                // Control always wins — PONG must never queue behind rate-limited chat.
                if (conn.ControlOut.Reader.TryRead(out var ctrl))
                {
                    await SendLineAsync(conn, ctrl, ct);
                    continue;
                }

                if (conn.ChatOut.Reader.TryPeek(out _))
                {
                    // Peek, don't take: while throttled the line stays queued and the loop keeps
                    // checking the control lane instead of sleeping up to 30s holding it.
                    var wait = _rateLimiter.TryReserve(DateTime.UtcNow);
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait < ThrottlePoll ? wait : ThrottlePoll, ct);
                        continue;
                    }

                    conn.ChatOut.Reader.TryRead(out var chat);
                    try
                    {
                        await SendLineAsync(conn, chat!.Line, ct);
                    }
                    catch
                    {
                        StatusChanged?.Invoke("Chat message not sent — connection dropped.");
                        throw;
                    }
                    // Echo only after the bytes are on the wire, so a failed write is never
                    // shown to the user as a sent message.
                    MessageReceived?.Invoke(chat.Echo);
                    continue;
                }

                // Releases can outnumber items (an item read on an earlier wake), so a spurious
                // wake just loops once and finds nothing.
                await conn.Signal.WaitAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* socket failed — the listener drives reconnect */ }
    }

    private static Task SendLineAsync(Connection conn, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        return conn.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    /// <summary>Queues a chat message to the joined channel as the authenticated user.</summary>
    public Task SendMessageAsync(string channel, string message)
    {
        Connection? conn;
        lock (_gate) conn = _active;

        if (conn is not { JoinConfirmed: true } || conn.Socket.State != WebSocketState.Open)
        {
            StatusChanged?.Invoke("Not connected to chat — message not sent.");
            return Task.CompletedTask;
        }

        // Twitch IRC does NOT echo your own PRIVMSG back, so the writer surfaces this locally
        // once it's actually written, through the same event the chat views listen to.
        var echo = new TwitchChatMessage
        {
            Username    = string.IsNullOrWhiteSpace(_desiredUsername) ? channel : _desiredUsername,
            Color       = "#00C9A7",   // accent teal so your own messages stand out
            Text        = message,
            EventType   = TwitchEventType.ChatMessage,
            IsLocalEcho = true,
            Time        = DateTime.Now
        };

        var line = $"PRIVMSG #{channel.ToLowerInvariant().TrimStart('#')} :{message}";
        if (!conn.ChatOut.Writer.TryWrite(new OutboundChat(line, echo)))
        {
            StatusChanged?.Invoke("Chat send queue is full — message dropped.");
            return Task.CompletedTask;
        }
        conn.Signal.Release();
        return Task.CompletedTask;
    }

    // ── Listen loop ──────────────────────────────────────────────────────────

    private const int MaxLineLength = 8192;   // guard against unbounded buffered garbage

    private async Task ListenLoopAsync(Connection conn)
    {
        var buf     = new byte[65536];
        var pending = new StringBuilder();   // retains a partial line ACROSS websocket frames
        // Stateful decoder: a multi-byte UTF-8 char (emoji, accents) split across two frames
        // decodes correctly instead of turning into two replacement characters.
        var decoder = Encoding.UTF8.GetDecoder();
        var chars   = new char[Encoding.UTF8.GetMaxCharCount(buf.Length)];

        try
        {
            while (conn.Socket.State == WebSocketState.Open && !conn.Cts.IsCancellationRequested)
            {
                var result = await conn.Socket.ReceiveAsync(buf, conn.Cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                int n = decoder.GetChars(buf, 0, result.Count, chars, 0, flush: false);
                pending.Append(chars, 0, n);

                // IRC is CRLF-delimited and one frame may carry several messages; a frame may
                // also end mid-line, so anything after the last CRLF stays buffered.
                var text = pending.ToString();
                int consumed = 0;
                while (true)
                {
                    int idx = text.IndexOf("\r\n", consumed, StringComparison.Ordinal);
                    if (idx < 0) break;
                    var line = text[consumed..idx];
                    consumed = idx + 2;
                    if (line.Length > 0) await HandleLineAsync(conn, line);
                }

                pending.Clear();
                var remainder = text[consumed..];
                if (remainder.Length > MaxLineLength)
                {
                    // Malformed/oversized frame — reset parser state rather than corrupting it.
                    StatusChanged?.Invoke("Protocol error (oversized line) — reconnecting…");
                    break;
                }
                pending.Append(remainder);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* socket closed */ }

        // Only the CURRENT generation may drive recovery — a retired listener exits silently.
        if (!IsCurrent(conn.Generation)) return;

        if (_shouldBeConnected && !_terminalFailure)
        {
            SetState(TwitchChatState.Reconnecting, "Connection lost — reconnecting…");
            ScheduleReconnect(conn.Generation);
        }
        else if (!_shouldBeConnected)
        {
            SetState(TwitchChatState.Stopped, "Disconnected from chat");
        }
    }

    // ── Reconnect supervisor ─────────────────────────────────────────────────

    private void ScheduleReconnect(int generation)
    {
        if (!IsCurrent(generation)) return;
        _ = Task.Run(ReconnectLoopAsync);
    }

    private int _reconnectRunning;   // 0/1 guard — only one supervisor at a time

    private async Task ReconnectLoopAsync()
    {
        if (Interlocked.CompareExchange(ref _reconnectRunning, 1, 0) != 0) return;
        try
        {
            while (_shouldBeConnected && !_terminalFailure && _reconnectAttempts < _backoffSeconds.Length)
            {
                int delay = _backoffSeconds[_reconnectAttempts];
                _reconnectAttempts++;
                SetState(TwitchChatState.Reconnecting,
                         $"Reconnecting to chat in {delay}s (attempt {_reconnectAttempts}/{_backoffSeconds.Length})…");

                try { await Task.Delay(TimeSpan.FromSeconds(delay)); } catch { }
                if (!_shouldBeConnected || _terminalFailure) return;

                await OpenConnectionAsync();

                // Wait for the JOIN to confirm — socket-open alone is NOT success.
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    if (IsConnected) return;                       // confirmed rejoin
                    if (_terminalFailure || !_shouldBeConnected) return;
                    await Task.Delay(200);
                }
            }

            if (_shouldBeConnected && !_terminalFailure)
                StatusChanged?.Invoke("⚠  Could not reconnect to chat. Check your connection or re-link Twitch in Settings.");
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectRunning, 0);
        }
    }

    // ── IRC line handling ────────────────────────────────────────────────────

    private async Task HandleLineAsync(Connection conn, string rawLine)
    {
        var irc = IrcParser.ParseLine(rawLine);

        // PING must be answered on the control lane regardless of state.
        if (irc.Command.Equals("PING", StringComparison.OrdinalIgnoreCase))
        {
            EnqueueControl(conn, $"PONG :{(string.IsNullOrEmpty(irc.Trailing) ? "tmi.twitch.tv" : irc.Trailing)}");
            return;
        }

        switch (irc.Command.ToUpperInvariant())
        {
            // 001 = welcome (authenticated). 366 = end of NAMES for a channel (join complete).
            case "001":
                StatusChanged?.Invoke("Authenticated with Twitch…");
                return;

            case "366":
            case "JOIN":
                if (!conn.JoinConfirmed && IsCurrent(conn.Generation))
                {
                    conn.JoinConfirmed = true;
                    _reconnectAttempts = 0;             // confirmed rejoin resets the ladder
                    SetState(TwitchChatState.Connected, "Connected — reading live chat");
                    Connected?.Invoke();
                }
                return;

            case "NOTICE":
                HandleNotice(irc);
                return;

            case "RECONNECT":
                // Twitch is asking us to reconnect — honour it without treating it as an error.
                StatusChanged?.Invoke("Twitch requested reconnect…");
                return;

            case "PRIVMSG":
            {
                var msg = IrcParser.ParsePrivmsg(irc);
                if (msg is not null && !IsDuplicate(irc.Tags)) MessageReceived?.Invoke(msg);
                return;
            }

            case "USERNOTICE":
            {
                var msg = IrcParser.ParseUserNotice(irc.Tags);
                if (msg is not null && !IsDuplicate(irc.Tags)) MessageReceived?.Invoke(msg);
                return;
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Classifies NOTICE by its msg-id tag rather than substring matching (review Twitch-03).
    /// Only genuine auth/channel failures are terminal — every other notice is informational
    /// and must NOT disable the reconnect supervisor.
    /// </summary>
    private void HandleNotice(IrcLine irc)
    {
        var msgId  = IrcParser.ParseTags(irc.Tags).MsgId;
        var text   = irc.Trailing;

        bool authFailed = msgId is "login_failed" or "invalid_user"
                       || text.Contains("Login authentication failed", StringComparison.OrdinalIgnoreCase)
                       || text.Contains("Improperly formatted auth", StringComparison.OrdinalIgnoreCase);

        bool channelGone = msgId is "msg_channel_suspended" or "msg_banned" or "msg_channel_blocked"
                        || text.Contains("channel is suspended", StringComparison.OrdinalIgnoreCase);

        if (authFailed)
        {
            _terminalFailure = true;
            SetState(TwitchChatState.AuthenticationFailed,
                     "⚠  Twitch login failed — reconnect your account in Settings.");
            return;
        }

        if (channelGone)
        {
            _terminalFailure = true;
            SetState(TwitchChatState.ChannelUnavailable,
                     "⚠  That Twitch channel is unavailable.");
            return;
        }

        // Everything else (slow mode, msg rejected, etc.) is informational only.
        if (!string.IsNullOrWhiteSpace(text)) StatusChanged?.Invoke(text);
    }

    // ── Duplicate suppression (review Twitch-06) ─────────────────────────────

    private readonly MessageDeduplicator _dedup = new();

    private bool IsDuplicate(string tags) => _dedup.IsDuplicate(IrcParser.ParseTags(tags).Id, DateTime.UtcNow);
}
