using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamCommand.Services;

public enum OBSState { Disconnected, Connecting, Connected, Error, Reconnecting }

/// <summary>
/// A request OBS accepted but refused to execute — e.g. unsupported request, invalid
/// parameters, or a runtime failure. Distinct from a transport failure: the socket is
/// healthy, so this must NOT trigger a reconnect (review OBS-04).
/// </summary>
public readonly record struct ObsRequestFailure(string RequestType, int Code, string Comment);

/// <summary>
/// Communicates with OBS Studio via the built-in obs-websocket v5 server.
/// Enable in OBS: Tools → WebSocket Server Settings → Enable WebSocket Server.
/// No external library or subscription needed — it's free and built into OBS 28+.
/// </summary>
public class OBSWebSocketService
{
    /// <summary>App-wide shared OBS connection. Use this everywhere — never create a new instance in a view.</summary>
    public static readonly OBSWebSocketService Shared = new();
    private ClientWebSocket?         _ws;
    private CancellationTokenSource? _cts;

    // ── Connection generation (reliability review OBS-02) ─────────────────────
    // Every connection attempt takes the next generation number. Listener and fetch tasks
    // capture their own generation and must check IsCurrentGeneration before mutating any
    // shared state — otherwise a retired listener can cancel a fresh connection's pending
    // requests or spawn a duplicate reconnect loop.
    private int _generation;
    private readonly object _connGate = new();

    private bool IsCurrentGeneration(int generation)
    {
        lock (_connGate) return _generation == generation;
    }

    // ── Auto-reconnect state ──────────────────────────────────────────────────
    // Backoff schedule: 3s → 6s → 12s → 24s → 30s, then give up after 5 attempts.
    private static readonly int[] _backoffSeconds = { 3, 6, 12, 24, 30 };
    private bool   _userInitiatedDisconnect;
    private int    _reconnectAttempts;
    private int    _reconnectRunning;   // 0/1 guard — only one reconnect supervisor at a time
    private string _lastHost     = "localhost";
    private int    _lastPort      = 4455;
    private string _lastPassword  = "";

    /// <summary>Fired after a reconnect has re-queried OBS and republished authoritative state.</summary>
    public event Action? StateSynchronized;

    /// <summary>Fired when OBS rejects a request (review OBS-04) — an operation failure, not a transport failure.</summary>
    public event Action<ObsRequestFailure>? RequestFailed;

    // ── Audio meter coalescing (review OBS-06) ───────────────────────────────
    // InputVolumeMeters arrives ~20×/second. The receive loop only stores the newest
    // sample; this publisher drains it at a fixed rate so a slow UI handler can never
    // back-pressure the control plane. Missed samples are intentionally dropped.
    private Dictionary<string, float>? _latestAudioLevels;
    private static readonly TimeSpan AudioPublishInterval = TimeSpan.FromMilliseconds(50);   // 20 Hz

    private async Task AudioPublishLoopAsync(int generation, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsCurrentGeneration(generation))
            {
                await Task.Delay(AudioPublishInterval, ct);

                var sample = System.Threading.Interlocked.Exchange(ref _latestAudioLevels, null);
                if (sample is { Count: > 0 }) AudioLevelsUpdated?.Invoke(sample);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    // Pending request-response pairs keyed by requestId
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode>>
        _pending = new();

    public OBSState State        { get; private set; } = OBSState.Disconnected;
    public bool     IsStreaming  { get; private set; }
    public string   StatusMessage{ get; private set; } = "OBS: Not connected";

    public event Action<OBSState>?                          StateChanged;
    public event Action<bool>?                              StreamingStateChanged;      // true = stream started
    public event Action<string[]>?                          ScenesLoaded;               // fires when scene list is fetched
    public event Action<(string[] Mics, string[] Outputs)>? AudioInputsLoaded;          // fires after scene list
    /// <summary>
    /// Fired ~20×/second while OBS is connected.
    /// Key = OBS input name, Value = peak level 0.0–1.0 (max across all channels).
    /// </summary>
    public event Action<Dictionary<string, float>>?         AudioLevelsUpdated;

    // ── Public API ──────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(string host = "localhost", int port = 4455, string password = "")
    {
        if (State == OBSState.Connected) return true;

        // Audit SC-05: obs-websocket is a plaintext ws:// protocol and its auth exchange is
        // password-derived. Restrict to loopback so control traffic and that exchange can
        // never cross a network unencrypted. Remote OBS would require wss:// + cert
        // validation, which this client does not implement.
        if (!IsLoopbackHost(host))
        {
            SetState(OBSState.Error,
                     $"OBS: '{host}' is not allowed — only local OBS connections are supported.");
            return false;
        }

        if (port < 1 || port > 65535)
        {
            SetState(OBSState.Error, $"OBS: invalid port {port}.");
            return false;
        }

        // Remember params so the auto-reconnect loop can re-dial the same server.
        _lastHost     = host;
        _lastPort     = port;
        _lastPassword = password;
        _userInitiatedDisconnect = false;

        SetState(State == OBSState.Reconnecting ? OBSState.Reconnecting : OBSState.Connecting,
                 "OBS: Connecting…");

        // Claim a new generation and build this connection's own socket + token, so a
        // previous listener can never act on them (review OBS-02).
        int generation;
        ClientWebSocket socket;
        CancellationTokenSource cts;
        lock (_connGate)
        {
            _generation++;
            generation = _generation;
            _ws?.Dispose();
            _cts?.Dispose();
            socket = new ClientWebSocket();
            cts    = new CancellationTokenSource();
            _ws    = socket;
            _cts   = cts;
        }

        try
        {
            // Each handshake phase gets its own bounded deadline (review OBS-03). Previously
            // only the TCP connect was time-boxed, so an OBS that accepted the socket but
            // never sent Hello would hang here forever — and, now that reconnect exists,
            // stall the entire backoff ladder.
            await WithDeadlineAsync(
                ct => socket.ConnectAsync(new Uri($"ws://{host}:{port}"), ct),
                TimeSpan.FromSeconds(5), cts.Token, "connect");

            // Step 1 — receive Hello (op 0)
            var hello = await WithDeadlineAsync(
                ct => ReceiveAsync(socket, ct), TimeSpan.FromSeconds(5), cts.Token, "Hello");
            if (hello is null) throw new Exception("OBS sent no Hello message.");

            // Step 2 — build auth string if OBS has a password set
            string authString = "";
            var authNode = hello["d"]?["authentication"];
            if (authNode is not null && !string.IsNullOrWhiteSpace(password))
            {
                var salt      = authNode["salt"]!.GetValue<string>();
                var challenge = authNode["challenge"]!.GetValue<string>();
                authString = BuildAuth(password, salt, challenge);
            }

            // Step 3 — send Identify (op 1)
            // eventSubscriptions: 2047 = All standard events; 65536 = InputVolumeMeters (high-volume, must opt in explicitly)
            const int EventSubs = 2047 | 65536;
            object identData = string.IsNullOrEmpty(authString)
                ? (object)new { rpcVersion = 1, eventSubscriptions = EventSubs }
                : new { rpcVersion = 1, authentication = authString, eventSubscriptions = EventSubs };

            await SendAsync(new { op = 1, d = identData });

            // Step 4 — receive Identified (op 2)
            var identified = await WithDeadlineAsync(
                ct => ReceiveAsync(socket, ct), TimeSpan.FromSeconds(5), cts.Token, "Identify");
            if (identified?["op"]?.GetValue<int>() != 2)
                throw new Exception("OBS rejected the connection. Check your WebSocket password in Settings.");

            // Another connection superseded us mid-handshake — abandon quietly.
            if (!IsCurrentGeneration(generation)) return false;

            SetState(OBSState.Connected, "OBS: Connected ✓");

            // Own the listener task for this generation only.
            _ = Task.Run(() => ListenLoopAsync(socket, cts, generation));

            // Audio meters are published on their own cadence, off the receive loop.
            _ = Task.Run(() => AudioPublishLoopAsync(generation, cts.Token));

            // Reconcile authoritative state before declaring the backoff ladder healthy
            // (review OBS-05): events missed while offline are recovered here.
            _ = Task.Run(() => ReconcileStateAsync(generation));

            return true;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentGeneration(generation))
                SetState(OBSState.Error, "OBS: Connection timed out — is OBS open?");
            return false;
        }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
                SetState(OBSState.Error, $"OBS: {ex.Message}");
            return false;
        }
    }

    /// <summary>Runs <paramref name="op"/> under a bounded deadline linked to the connection token.</summary>
    private static async Task<T> WithDeadlineAsync<T>(
        Func<CancellationToken, Task<T>> op, TimeSpan deadline, CancellationToken outer, string phase)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outer);
        linked.CancelAfter(deadline);
        try { return await op(linked.Token); }
        catch (OperationCanceledException) when (!outer.IsCancellationRequested)
        {
            throw new TimeoutException($"OBS did not complete '{phase}' within {deadline.TotalSeconds:0}s.");
        }
    }

    private static async Task WithDeadlineAsync(
        Func<CancellationToken, Task> op, TimeSpan deadline, CancellationToken outer, string phase)
        => await WithDeadlineAsync<bool>(async ct => { await op(ct); return true; }, deadline, outer, phase);

    /// <summary>
    /// Re-queries everything the UI treats as authoritative. Runs after every successful
    /// connect (including reconnects) because events fired while we were offline are lost.
    /// The backoff ladder only resets once this completes (review OBS-01/OBS-05).
    /// </summary>
    private async Task ReconcileStateAsync(int generation)
    {
        try
        {
            await FetchScenesAsync();
            if (!IsCurrentGeneration(generation)) return;

            await FetchAudioInputsAsync();
            if (!IsCurrentGeneration(generation)) return;

            // Streaming status — a stream may have started or stopped while we were away.
            var streamResp = await SendRequestWithResponseAsync("GetStreamStatus");
            var streaming  = streamResp?["d"]?["responseData"]?["outputActive"]?.GetValue<bool>();
            if (streaming is bool live && live != IsStreaming)
            {
                IsStreaming = live;
                StreamingStateChanged?.Invoke(live);
            }

            if (!IsCurrentGeneration(generation)) return;
            await GetVirtualCamStatusAsync();

            if (!IsCurrentGeneration(generation)) return;
            _reconnectAttempts = 0;   // healthy, fully-reconciled connection resets the ladder
            StateSynchronized?.Invoke();
        }
        catch { /* reconciliation is best-effort; the listener drives recovery */ }
    }

    public bool IsVirtualCamActive { get; private set; }

    public async Task StartStreamAsync()
    {
        if (State != OBSState.Connected) return;
        await SendRequestAsync("StartStream");
    }

    public async Task StopStreamAsync()
    {
        if (State != OBSState.Connected) return;
        await SendRequestAsync("StopStream");
    }

    public async Task StartVirtualCamAsync()
    {
        if (State != OBSState.Connected) return;
        await SendRequestAsync("StartVirtualCam");
        IsVirtualCamActive = true;
    }

    public async Task StopVirtualCamAsync()
    {
        if (State != OBSState.Connected) return;
        await SendRequestAsync("StopVirtualCam");
        IsVirtualCamActive = false;
    }

    /// <summary>Queries OBS for virtual camera state and updates <see cref="IsVirtualCamActive"/>.</summary>
    public async Task<bool> GetVirtualCamStatusAsync()
    {
        var resp   = await SendRequestWithResponseAsync("GetVirtualCamStatus");
        var active = resp?["d"]?["responseData"]?["outputActive"]?.GetValue<bool>() ?? false;
        IsVirtualCamActive = active;
        return active;
    }

    public async Task<string[]> GetScenesAsync()
    {
        var resp   = await SendRequestWithResponseAsync("GetSceneList");
        var scenes = resp?["d"]?["responseData"]?["scenes"]?.AsArray();
        if (scenes is null) return Array.Empty<string>();

        var names = new List<string>();
        foreach (var scene in scenes)
        {
            var name = scene?["sceneName"]?.GetValue<string>();
            if (name is not null) names.Add(name);
        }
        // OBS returns scenes in reverse order (bottom of list first)
        names.Reverse();
        return names.ToArray();
    }

    public async Task SetSceneAsync(string sceneName)
        => await SendRequestWithResponseAsync("SetCurrentProgramScene", new { sceneName });

    /// <summary>Mutes or unmutes an OBS audio input by name.</summary>
    public async Task SetInputMuteAsync(string inputName, bool muted)
    {
        if (State != OBSState.Connected || string.IsNullOrEmpty(inputName)) return;
        await SendRequestAsync("SetInputMute", new { inputName, inputMuted = muted });
    }

    /// <summary>Sets the volume of an OBS audio input. <paramref name="volumeMul"/> is 0.0–1.0.</summary>
    public async Task SetInputVolumeAsync(string inputName, double volumeMul)
    {
        if (State != OBSState.Connected || string.IsNullOrEmpty(inputName)) return;
        await SendRequestAsync("SetInputVolume", new { inputName, inputVolumeMul = Math.Clamp(volumeMul, 0.0, 1.0) });
    }

    /// <summary>Returns audio inputs grouped by type (mics vs desktop/output).</summary>
    public async Task<(string[] Mics, string[] Outputs)> GetAudioInputsAsync()
    {
        var resp   = await SendRequestWithResponseAsync("GetInputList");
        var inputs = resp?["d"]?["responseData"]?["inputs"]?.AsArray();
        if (inputs is null) return (Array.Empty<string>(), Array.Empty<string>());

        var mics    = new List<string>();
        var outputs = new List<string>();

        foreach (var input in inputs)
        {
            var name = input?["inputName"]?.GetValue<string>();
            var kind = input?["inputKind"]?.GetValue<string>()  ?? "";
            if (name is null) continue;

            // OBS Windows source kinds
            if (kind.Contains("wasapi_input") || kind.Contains("coreaudio_input") || kind.Contains("dshow_input"))
                mics.Add(name);
            else if (kind.Contains("wasapi_output") || kind.Contains("coreaudio_output"))
                outputs.Add(name);
        }
        return (mics.ToArray(), outputs.ToArray());
    }

    public async Task DisconnectAsync()
    {
        _userInitiatedDisconnect = true;   // suppress the auto-reconnect loop
        _reconnectAttempts       = 0;

        ClientWebSocket? socket;
        CancellationTokenSource? cts;
        lock (_connGate)
        {
            _generation++;   // retire the current generation so its listener goes quiet
            socket = _ws;  _ws  = null;
            cts    = _cts; _cts = null;
        }

        try { cts?.Cancel(); } catch { }
        if (socket?.State == WebSocketState.Open)
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        try { socket?.Dispose(); } catch { }
        try { cts?.Dispose(); }    catch { }

        SetState(OBSState.Disconnected, "OBS: Disconnected");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task ListenLoopAsync(ClientWebSocket socket, CancellationTokenSource cts, int generation)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                var msg = await ReceiveAsync(socket, cts.Token);
                if (msg is null) break;

                var op = msg["op"]?.GetValue<int>() ?? -1;

                if (op == 5)   // Event
                {
                    var evType = msg["d"]?["eventType"]?.GetValue<string>();
                    if (evType == "StreamStateChanged")
                    {
                        var active = msg["d"]?["eventData"]?["outputActive"]?.GetValue<bool>() ?? false;
                        IsStreaming = active;
                        StreamingStateChanged?.Invoke(active);
                    }
                    else if (evType == "SceneCreated" || evType == "SceneRemoved" || evType == "SceneNameChanged")
                    {
                        // Re-fetch the scene list whenever it changes in OBS
                        _ = Task.Run(FetchScenesAsync);
                    }
                    else if (evType == "InputVolumeMeters")
                    {
                        // ~20 Hz push event — parse peak level for each input
                        var inputs = msg["d"]?["eventData"]?["inputs"]?.AsArray();
                        if (inputs is not null && AudioLevelsUpdated is not null)
                        {
                            var levels = new Dictionary<string, float>(
                                inputs.Count, StringComparer.OrdinalIgnoreCase);

                            foreach (var input in inputs)
                            {
                                var name     = input?["inputName"]?.GetValue<string>();
                                var channels = input?["inputLevelsMul"]?.AsArray();
                                if (name is null || channels is null) continue;

                                float peak = 0f;
                                foreach (var channel in channels)
                                {
                                    // Each channel array: [magnitude, peak, inputPeak]
                                    var ch = channel?.AsArray();
                                    if (ch is null || ch.Count < 2) continue;
                                    var p = ch[1]?.GetValue<float>() ?? 0f;
                                    if (p > peak) peak = p;
                                }
                                levels[name] = peak;
                            }

                            // Do NOT invoke subscribers inline (review OBS-06). These arrive
                            // ~20×/second and a slow UI handler would stall the receive loop,
                            // delaying stream-state events, request responses, and close
                            // signals. Store the newest sample; a publisher loop drains it at
                            // a controlled rate, coalescing anything it missed.
                            if (levels.Count > 0)
                                System.Threading.Volatile.Write(ref _latestAudioLevels, levels);
                        }
                    }
                }
                else if (op == 7)   // RequestResponse — complete the pending awaiter
                {
                    var id = msg["d"]?["requestId"]?.GetValue<string>();

                    // Surface protocol-level request failures instead of letting callers
                    // silently treat them as no-ops (review OBS-04).
                    var status = msg["d"]?["requestStatus"];
                    if (status?["result"]?.GetValue<bool>() == false)
                    {
                        RequestFailed?.Invoke(new ObsRequestFailure(
                            RequestType: msg["d"]?["requestType"]?.GetValue<string>() ?? "(unknown)",
                            Code:        status["code"]?.GetValue<int>() ?? 0,
                            Comment:     status["comment"]?.GetValue<string>() ?? ""));
                    }

                    if (id is not null && _pending.TryRemove(id, out var tcs))
                        tcs.TrySetResult(msg);
                }
            }
        }
        catch { /* socket closed or cancelled */ }
        finally
        {
            // A RETIRED listener must not touch shared state (review OBS-02). _pending is
            // shared across generations, so clearing it from a stale listener would cancel
            // the *new* connection's in-flight requests — scene switches and stream start
            // would fail mysteriously right after a reconnect.
            if (IsCurrentGeneration(generation))
            {
                // Cancel all pending requests so callers don't hang
                foreach (var kv in _pending)
                    kv.Value.TrySetCanceled();
                _pending.Clear();

                // If the socket dropped while we were connected and the user didn't ask
                // for it, kick off the exponential-backoff reconnect loop.
                if (State == OBSState.Connected && !_userInitiatedDisconnect)
                {
                    SetState(OBSState.Reconnecting, "OBS: Connection lost — reconnecting…");
                    _ = Task.Run(ReconnectLoopAsync);
                }
                else if (State == OBSState.Connected)
                {
                    SetState(OBSState.Disconnected, "OBS: Disconnected");
                }
            }

            try { socket.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Re-dials the last OBS server with exponential backoff (3s → 6s → 12s → 24s → 30s).
    /// Gives up after 5 attempts and lands in the Error state so the user can retry manually.
    /// </summary>
    private async Task ReconnectLoopAsync()
    {
        // Only one supervisor may run — otherwise two loops race the backoff ladder.
        if (Interlocked.CompareExchange(ref _reconnectRunning, 1, 0) != 0) return;
        try
        {
        while (_reconnectAttempts < _backoffSeconds.Length && !_userInitiatedDisconnect)
        {
            int delay = _backoffSeconds[_reconnectAttempts];
            _reconnectAttempts++;
            SetState(OBSState.Reconnecting,
                     $"OBS: Reconnecting in {delay}s (attempt {_reconnectAttempts}/{_backoffSeconds.Length})…");

            try { await Task.Delay(TimeSpan.FromSeconds(delay)); } catch { }
            if (_userInitiatedDisconnect) return;

            SetState(OBSState.Reconnecting, $"OBS: Reconnecting (attempt {_reconnectAttempts})…");
            bool ok = await ConnectAsync(_lastHost, _lastPort, _lastPassword);
            if (ok) return;   // ReconcileStateAsync resets _reconnectAttempts once synced
        }

        if (!_userInitiatedDisconnect)
            SetState(OBSState.Error, "OBS: Could not reconnect. Open Live Control to retry.");
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectRunning, 0);
        }
    }

    private async Task FetchScenesAsync()
    {
        var scenes = await GetScenesAsync();
        if (scenes.Length > 0)
            ScenesLoaded?.Invoke(scenes);
    }

    private async Task FetchAudioInputsAsync()
    {
        var (mics, outputs) = await GetAudioInputsAsync();
        if (mics.Length > 0 || outputs.Length > 0)
            AudioInputsLoaded?.Invoke((mics, outputs));
    }

    private async Task<JsonNode?> SendRequestWithResponseAsync(string requestType, object? requestData = null)
    {
        if (State != OBSState.Connected) return null;

        var id  = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        object payload = requestData is null
            ? (object)new { op = 6, d = new { requestType, requestId = id } }
            : new { op = 6, d = new { requestType, requestId = id, requestData } };

        await SendAsync(payload);

        // Give OBS 5 seconds to respond
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        timeout.Token.Register(() => { tcs.TrySetCanceled(); _pending.TryRemove(id, out _); });

        try   { return await tcs.Task; }
        catch { _pending.TryRemove(id, out _); return null; }
    }

    private async Task SendRequestAsync(string requestType, object? requestData = null)
    {
        object payload = requestData is null
            ? (object)new { op = 6, d = new { requestType, requestId = Guid.NewGuid().ToString("N") } }
            : new { op = 6, d = new { requestType, requestId = Guid.NewGuid().ToString("N"), requestData } };
        await SendAsync(payload);
    }

    private async Task SendAsync(object obj)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
    }

    // SECURITY 2: cap inbound OBS messages at 4 MB — prevents unbounded memory growth
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Reads a complete WebSocket message, handling multi-frame fragmentation.
    /// OBS can send scene-list responses larger than a single frame for complex setups.
    /// Throws InvalidOperationException if the total message exceeds 4 MB.
    /// </summary>
    private static async Task<JsonNode?> ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var sb     = new StringBuilder();
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (sb.Length + result.Count > MaxMessageBytes)
                throw new InvalidOperationException("OBS message exceeded 4 MB size limit.");
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return JsonNode.Parse(sb.ToString());
    }

    // OBS WebSocket v5 auth: base64(sha256(base64(sha256(password+salt))+challenge))
    private static string BuildAuth(string password, string salt, string challenge)
    {
        var step1 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(step1 + challenge)));
    }

    /// <summary>
    /// True when <paramref name="host"/> refers to this machine. Guards the ws:// transport
    /// against being pointed at a remote OBS instance (audit SC-05).
    /// </summary>
    private static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim().Trim('[', ']');   // tolerate bracketed IPv6

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("::1", StringComparison.Ordinal)) return true;

        return System.Net.IPAddress.TryParse(host, out var ip) &&
               System.Net.IPAddress.IsLoopback(ip);
    }

    private void SetState(OBSState state, string message)
    {
        State         = state;
        StatusMessage = message;
        StateChanged?.Invoke(state);
    }
}
