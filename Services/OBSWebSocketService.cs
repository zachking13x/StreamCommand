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

public enum OBSState { Disconnected, Connecting, Connected, Error }

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

    // Pending request-response pairs keyed by requestId
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode>>
        _pending = new();

    public OBSState State        { get; private set; } = OBSState.Disconnected;
    public bool     IsStreaming  { get; private set; }
    public string   StatusMessage{ get; private set; } = "OBS: Not connected";

    public event Action<OBSState>?                   StateChanged;
    public event Action<bool>?                       StreamingStateChanged;         // true = stream started
    public event Action<string[]>?                   ScenesLoaded;                  // fires when scene list is fetched
    public event Action<(string[] Mics, string[] Outputs)>? AudioInputsLoaded;     // fires after scene list

    // ── Public API ──────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(string host = "localhost", int port = 4455, string password = "")
    {
        if (State == OBSState.Connected) return true;

        SetState(OBSState.Connecting, "OBS: Connecting…");
        try
        {
            // Dispose any leftover token sources before creating new ones
            _cts?.Dispose();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            _ws?.Dispose();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri($"ws://{host}:{port}"), _cts.Token);

            // Reset to a non-expiring token now that the TCP connection is up
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            // Step 1 — receive Hello (op 0)
            var hello = await ReceiveAsync();
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
            object identData = string.IsNullOrEmpty(authString)
                ? (object)new { rpcVersion = 1 }
                : new { rpcVersion = 1, authentication = authString };

            await SendAsync(new { op = 1, d = identData });

            // Step 4 — receive Identified (op 2)
            var identified = await ReceiveAsync();
            if (identified?["op"]?.GetValue<int>() != 2)
                throw new Exception("OBS rejected the connection. Check your WebSocket password in Settings.");

            SetState(OBSState.Connected, "OBS: Connected ✓");

            // Start the listen loop then immediately fetch the scene list and audio inputs
            _ = Task.Run(ListenLoopAsync);
            _ = Task.Run(FetchScenesAsync);
            _ = Task.Run(FetchAudioInputsAsync);

            return true;
        }
        catch (OperationCanceledException)
        {
            SetState(OBSState.Error, "OBS: Connection timed out — is OBS open?");
            return false;
        }
        catch (Exception ex)
        {
            SetState(OBSState.Error, $"OBS: {ex.Message}");
            return false;
        }
    }

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
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
        SetState(OBSState.Disconnected, "OBS: Disconnected");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task ListenLoopAsync()
    {
        try
        {
            while (_ws?.State == WebSocketState.Open && !(_cts?.IsCancellationRequested ?? true))
            {
                var msg = await ReceiveAsync();
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
                }
                else if (op == 7)   // RequestResponse — complete the pending awaiter
                {
                    var id = msg["d"]?["requestId"]?.GetValue<string>();
                    if (id is not null && _pending.TryRemove(id, out var tcs))
                        tcs.TrySetResult(msg);
                }
            }
        }
        catch { /* socket closed or cancelled */ }
        finally
        {
            // Cancel all pending requests so callers don't hang
            foreach (var kv in _pending)
                kv.Value.TrySetCanceled();
            _pending.Clear();

            if (State == OBSState.Connected)
                SetState(OBSState.Disconnected, "OBS: Disconnected unexpectedly");
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
    private async Task<JsonNode?> ReceiveAsync()
    {
        if (_ws is null) return null;

        var buffer = new byte[65536];
        var sb     = new StringBuilder();
        WebSocketReceiveResult result;

        do
        {
            result = await _ws.ReceiveAsync(buffer, _cts?.Token ?? CancellationToken.None);
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

    private void SetState(OBSState state, string message)
    {
        State         = state;
        StatusMessage = message;
        StateChanged?.Invoke(state);
    }
}
