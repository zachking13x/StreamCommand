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


    /// <summary>
    /// Connects to OBS using the saved settings, or does nothing if OBS is already up on the
    /// same endpoint.
    ///
    /// Why this exists: OBS used to connect ONLY from LiveControlView.Loaded, and the views
    /// are built lazily — so on every launch the app sat disconnected until the user happened
    /// to open Live Control. From the dashboard it looked like "I have to reconnect OBS every
    /// time I close Stream Command". Same bug class as
    /// <see cref="TwitchChatService.StartFromSettingsAsync"/>, which was fixed first.
    ///
    /// Auto-connect is limited to users who have connected successfully before
    /// (<see cref="AppSettings.OBSEverConnected"/>) so someone who has never set OBS up
    /// doesn't meet a connection error on first run. Pass <paramref name="force"/> after the
    /// user edits the OBS port or password to apply the change immediately.
    /// </summary>
    public async Task<bool> StartFromSettingsAsync(bool force = false)
    {
#if DEBUG
        if (Dev.StoreCapture.Active) return false;   // demo data only — never touch a real OBS
#endif
        var s = SettingsService.Load();
        if (!force && !s.OBSEverConnected) return false;

        bool sameEndpoint = _lastPort == s.OBSWebSocketPort && _lastPassword == s.OBSWebSocketPassword;
        bool active       = State is OBSState.Connected or OBSState.Connecting or OBSState.Reconnecting;

        if (active && sameEndpoint) return State == OBSState.Connected;

        // ConnectAsync short-circuits while already Connected, so changed credentials only
        // take effect after dropping the current socket.
        if (active && !sameEndpoint) await DisconnectAsync();

        if (await ConnectAsync("localhost", s.OBSWebSocketPort, s.OBSWebSocketPassword)) return true;

        // OBS wasn't there. Start it ourselves rather than sending the streamer off to open
        // another app — that is the entire point of this product.
        return await TryAutoLaunchAndConnectAsync(s.OBSWebSocketPort, s.OBSWebSocketPassword);
    }


    /// <summary>
    /// Starts OBS if it is not already running, then waits for its WebSocket to accept us.
    ///
    /// The product promise is that you do not open a million apps — so when Stream Command
    /// needs OBS and OBS is closed, it opens it rather than telling the streamer to go do it.
    ///
    /// Guarded carefully:
    ///  - never launches when an obs64 process already exists, otherwise OBS shows its own
    ///    "OBS is already running" dialog, which is worse than doing nothing;
    ///  - honours <see cref="AppSettings.AutoLaunchOBS"/> so it can be turned off;
    ///  - OBS can take a while to boot, so it retries the connect rather than failing once.
    /// </summary>
    private async Task<bool> TryAutoLaunchAndConnectAsync(int port, string password)
    {
        if (!SettingsService.Load().AutoLaunchOBS) return false;
        if (IsObsRunning()) return false;   // already up — its WebSocket just isn't reachable

        SetState(OBSState.Connecting, "OBS: Starting OBS…");

        // WE opened OBS, so it must not land on top of what the streamer is doing — but a
        // tray-only OBS was invisible in practice: Windows 11 tucks new tray icons into the ^
        // overflow, so the only sign OBS was running was Task Manager. So:
        //  - full-screen game in front → tray (never disturb the game) + a toast saying where it went;
        //  - otherwise → minimized on the taskbar, where it can be seen and clicked.
        // Showing a tray-hidden OBS ourselves isn't an option: Qt doesn't repaint a window
        // un-hidden behind its back (it comes up black), and a second launch starts a second OBS.
        var (gaming, previousForeground) = System.Windows.Application.Current?.Dispatcher.Invoke(
            () => (ForegroundWindow.IsOtherAppFullScreen(), ForegroundWindow.Current)) ?? (false, IntPtr.Zero);

        var (launched, message) = gaming
            ? AppLaunchService.TryLaunch("obs", "--minimize-to-tray")
            : AppLaunchService.TryLaunch("obs", minimized: true);
        if (!launched)
        {
            SetState(OBSState.Error, $"OBS: {message}");
            return false;
        }
        if (gaming)
            HotkeyCommands.Say("OBS started in the background",
                "It's in the system tray (the ^ next to the clock) and Stream Command is connecting to it.");
        else
            _ = HandFocusBackAsync(previousForeground);

        // OBS cold-starts slowly; keep retrying until it answers or we give up.
        // OBS cold-start measured at ~42s on this machine, and it is slower on a loaded PC or
        // first run after an update. 45s only just made it, so give it real headroom.
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline && !_userInitiatedDisconnect)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3)); } catch { }
            SetState(OBSState.Connecting, "OBS: Waiting for OBS to finish starting…");
            if (await ConnectAsync("localhost", port, password)) return true;
        }

        // Two common causes, and the user cannot guess either from "could not connect":
        //  - OBS is showing its crash-recovery prompt and is waiting on a click;
        //  - they chose Safe Mode, which DISABLES WebSockets entirely.
        SetState(OBSState.Error,
                 "OBS: Started OBS but it never answered. Check OBS for a prompt — and note that "
               + "Safe Mode disables WebSockets. Otherwise enable Tools → WebSocket Server Settings.");
        return false;
    }

    /// <summary>
    /// OBS activates its window when it finishes loading, even when started minimized, which
    /// would leave the streamer's keyboard pointed at a minimized OBS. Once OBS takes the
    /// foreground, give it back to whatever had it. Windows may refuse; then OBS just stays focused.
    /// </summary>
    private static async Task HandFocusBackAsync(IntPtr previous)
    {
        if (previous == IntPtr.Zero) return;
        var deadline = DateTime.UtcNow.AddSeconds(120);   // same budget as the connect wait
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            var fgPid = ForegroundWindow.CurrentProcessId();
            bool obsHasFocus;
            try { obsHasFocus = fgPid != 0 && System.Diagnostics.Process.GetProcessById((int)fgPid).ProcessName == "obs64"; }
            catch { obsHasFocus = false; }
            if (!obsHasFocus) continue;

            await Task.Delay(300);   // OBS may re-activate once more as its docks finish loading
            ForegroundWindow.TryRestore(previous);
            return;
        }
    }

    /// <summary>True when an OBS process already exists, whatever state its WebSocket is in.</summary>
    private static bool IsObsRunning()
    {
        try { return System.Diagnostics.Process.GetProcessesByName("obs64").Length > 0; }
        catch { return false; }
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

    /// <summary>OBS reported a mute change (any source). Raised on the socket thread — marshal to the UI.</summary>
    public event Action<string, bool>? InputMuteChanged;   // (inputName, muted)

    /// <summary>OBS switched program scene (any source). Raised on the socket thread — marshal to the UI.</summary>
    public event Action<string>? ProgramSceneChanged;

    /// <summary>Mutes or unmutes an OBS audio input by name.</summary>
    public async Task SetInputMuteAsync(string inputName, bool muted)
    {
        if (State != OBSState.Connected || string.IsNullOrEmpty(inputName)) return;
        await SendRequestAsync("SetInputMute", new { inputName, inputMuted = muted });
    }

    /// <summary>
    /// Flips an input's mute in OBS and returns the new state (null if OBS didn't answer).
    /// Uses OBS's own toggle so a mute changed inside OBS isn't overwritten by a stale local flag.
    /// </summary>
    public async Task<bool?> ToggleInputMuteAsync(string inputName)
    {
        if (State != OBSState.Connected || string.IsNullOrEmpty(inputName)) return null;
        var resp  = await SendRequestWithResponseAsync("ToggleInputMute", new { inputName });
        var muted = resp?["d"]?["responseData"]?["inputMuted"]?.GetValue<bool>();
        return muted;
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

    // ── Recording (used by Clips) ─────────────────────────────────────────────

    /// <summary>OBS recording started (true) or finished (false, with the file path). Raised on the socket thread.</summary>
    public event Action<bool, string?>? RecordingStateChanged;

    /// <summary>Whether OBS is recording and how far in, or null when OBS didn't answer.</summary>
    public async Task<(bool Active, TimeSpan Elapsed)?> GetRecordStatusAsync()
    {
        var data = (await SendRequestWithResponseAsync("GetRecordStatus"))?["d"]?["responseData"];
        if (data is null) return null;
        var active = data["outputActive"]?.GetValue<bool>() ?? false;
        var ms     = data["outputDuration"]?.GetValue<double>() ?? 0;
        return (active, TimeSpan.FromMilliseconds(ms));
    }

    /// <summary>Starts recording; returns OBS's refusal reason, or null on success.</summary>
    public async Task<string?> StartRecordAsync()
    {
        if (State != OBSState.Connected) return "OBS isn't connected.";
        var status = (await SendRequestWithResponseAsync("StartRecord"))?["d"]?["requestStatus"];
        if (status is null) return "OBS didn't answer.";
        return status["result"]?.GetValue<bool>() == true ? null : status["comment"]?.GetValue<string>() ?? "OBS refused to record.";
    }

    public async Task StopRecordAsync()
    {
        if (State != OBSState.Connected) return;
        await SendRequestWithResponseAsync("StopRecord");
    }

    /// <summary>
    /// Where the webcam sits on the OBS canvas, as 0–1 fractions, or null when the program scene
    /// has no visible Video Capture Device. Lets the vertical clip put the facecam on top without
    /// the streamer having to draw a box.
    /// </summary>
    public async Task<NormalizedRect?> GetFacecamRectAsync()
    {
        if (State != OBSState.Connected) return null;
        var video  = (await SendRequestWithResponseAsync("GetVideoSettings"))?["d"]?["responseData"];
        var scene  = (await SendRequestWithResponseAsync("GetCurrentProgramScene"))?["d"]?["responseData"]?["currentProgramSceneName"]?.GetValue<string>();
        double baseW = video?["baseWidth"]?.GetValue<double>() ?? 0, baseH = video?["baseHeight"]?.GetValue<double>() ?? 0;
        if (scene is null || baseW <= 0 || baseH <= 0) return null;

        var items = (await SendRequestWithResponseAsync("GetSceneItemList", new { sceneName = scene }))?["d"]?["responseData"]?["sceneItems"]?.AsArray();
        if (items is null) return null;

        foreach (var item in items)
        {
            if (item?["inputKind"]?.GetValue<string>() != "dshow_input") continue;
            if (item["sceneItemEnabled"]?.GetValue<bool>() == false) continue;
            var t = item["sceneItemTransform"];
            if (t is null) continue;
            var rect = FacecamMath.ToNormalized(
                t["positionX"]?.GetValue<double>() ?? 0, t["positionY"]?.GetValue<double>() ?? 0,
                t["width"]?.GetValue<double>() ?? 0,     t["height"]?.GetValue<double>() ?? 0,
                t["boundsType"]?.GetValue<string>() ?? "OBS_BOUNDS_NONE",
                t["boundsWidth"]?.GetValue<double>() ?? 0, t["boundsHeight"]?.GetValue<double>() ?? 0,
                t["alignment"]?.GetValue<int>() ?? 5, baseW, baseH);
            if (rect is not null) return rect;
        }
        return null;
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
                    // Program scene / mute changes come from OBS itself, so the UI follows a change
                    // made by a global shortcut, by our buttons, or by the streamer inside OBS alike.
                    else if (evType == "CurrentProgramSceneChanged")
                    {
                        var scene = msg["d"]?["eventData"]?["sceneName"]?.GetValue<string>();
                        if (scene is not null) ProgramSceneChanged?.Invoke(scene);
                    }
                    else if (evType == "InputMuteStateChanged")
                    {
                        var input = msg["d"]?["eventData"]?["inputName"]?.GetValue<string>();
                        var muted = msg["d"]?["eventData"]?["inputMuted"]?.GetValue<bool>();
                        if (input is not null && muted is bool m) InputMuteChanged?.Invoke(input, m);
                    }
                    else if (evType == "RecordStateChanged")
                    {
                        // Only the settled states matter; STARTING/STOPPING are transitional. The
                        // STOPPED event carries the finished file path, which clipping cuts from.
                        var data  = msg["d"]?["eventData"];
                        var state = data?["outputState"]?.GetValue<string>();
                        var path  = data?["outputPath"]?.GetValue<string>();
                        if (state == "OBS_WEBSOCKET_OUTPUT_STARTED")      RecordingStateChanged?.Invoke(true, path);
                        else if (state == "OBS_WEBSOCKET_OUTPUT_STOPPED") RecordingStateChanged?.Invoke(false, path);
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

        // Seed what's on program now — otherwise no tile is highlighted until the first switch.
        var current = await SendRequestWithResponseAsync("GetCurrentProgramScene");
        var name = current?["d"]?["responseData"]?["currentProgramSceneName"]?.GetValue<string>();
        if (name is not null) ProgramSceneChanged?.Invoke(name);
    }

    private async Task FetchAudioInputsAsync()
    {
        var (mics, outputs) = await GetAudioInputsAsync();
        if (mics.Length > 0 || outputs.Length > 0)
            AudioInputsLoaded?.Invoke((mics, outputs));

        // Seed the mic's real mute state — the UI used to assume "on" even when OBS had it muted.
        if (mics.Length > 0)
        {
            var resp  = await SendRequestWithResponseAsync("GetInputMute", new { inputName = mics[0] });
            var muted = resp?["d"]?["responseData"]?["inputMuted"]?.GetValue<bool>();
            if (muted is bool m) InputMuteChanged?.Invoke(mics[0], m);
        }
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
