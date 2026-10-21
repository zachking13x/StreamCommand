using System.IO;

namespace StreamCommand.Services;

public enum ClipSessionState { Idle, Starting, Recording, Rendering }

/// <summary>
/// Runs a clipping session: OBS records the stream, highlights are marked against the recording
/// clock while you play, and when recording stops each highlight is cut into a vertical short.
///
/// Working from OBS's own recording (not its replay buffer) is deliberate: the replay buffer is
/// off by default and can only be switched on inside OBS settings, while recording always works,
/// gives full quality, and doubles as "save my whole stream" for free.
/// </summary>
public sealed class ClipService
{
    public static ClipService Shared { get; } = new();

    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(45);
    private static OBSWebSocketService Obs => OBSWebSocketService.Shared;

    private readonly object _gate = new();
    private readonly List<ClipMoment> _moments = [];
    private ChatHypeDetector _chat = new(Cooldown);
    private AudioPeakDetector _audio = new(Cooldown);
    private string[] _mics = [];
    private bool _started;

    private bool _ownsRecording;     // only a recording WE started may ever be sent to the Recycle Bin
    private bool _fromStream;
    private bool _recordOnly;        // "save my full stream" with clipping off: record, mark nothing automatically
    private DateTime _recordStartWall;
    private NormalizedRect? _facecam;

    public ClipSessionState State { get; private set; } = ClipSessionState.Idle;
    public string Status { get; private set; } = "Not clipping";
    public DateTime? SessionStarted => State == ClipSessionState.Recording ? _recordStartWall : null;
    public bool FacecamDetected => _facecam is not null;
    public string? LastClipsFolder { get; private set; }
    public string? LastRecordingPath { get; private set; }

    public IReadOnlyList<ClipMoment> Moments { get { lock (_gate) return _moments.ToList(); } }

    /// <summary>Anything the Clips page shows changed. Raised on background threads.</summary>
    public event Action? Changed;

    public static string ClipsRoot(AppSettings s) => string.IsNullOrWhiteSpace(s.ClipsFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Stream Command Clips")
        : s.ClipsFolder;

    /// <summary>
    /// Wires into OBS + chat. Called once from MainWindow startup — never from the Clips view,
    /// which is lazy and would leave auto-clipping dead until the page was opened.
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        Obs.StreamingStateChanged  += live => _ = OnStreamingChangedAsync(live);
        Obs.RecordingStateChanged  += (active, path) => { if (!active) _ = OnRecordingStoppedAsync(path); };
        Obs.AudioInputsLoaded      += inputs => _mics = inputs.Mics;
        Obs.AudioLevelsUpdated     += OnAudioLevels;
        TwitchChatService.Shared.MessageReceived += OnChat;
    }

    // ── Session lifecycle ────────────────────────────────────────────────────

    private async Task OnStreamingChangedAsync(bool live)
    {
        var s = SettingsService.Load();
        if (live && State == ClipSessionState.Idle && (s.ClipWithStream || s.SaveFullStream))
        {
            var error = await StartSessionAsync(fromStream: true);
            if (error is not null) HotkeyCommands.Say("Clipping didn't start", error, warn: true);
        }
        else if (!live && State == ClipSessionState.Recording && _fromStream)
            await StopSessionAsync();
    }

    /// <summary>Starts recording-for-clips; returns why it couldn't, or null.</summary>
    public async Task<string?> StartSessionAsync(bool fromStream = false)
    {
        if (!FeatureGate.Has("clips")) return "Clips are part of the full app.";
        if (Obs.State != OBSState.Connected) return "Connect OBS first — clips are cut from its recording.";
        lock (_gate)
        {
            if (State != ClipSessionState.Idle) return null;
            State = ClipSessionState.Starting;
        }
        SetStatus("Starting…");

        var status = await Obs.GetRecordStatusAsync();
        if (status is null) return Fail("OBS didn't answer.");

        // Already recording (OBS's "record when streaming", or the streamer hit Record): ride
        // along on that file and never delete it — it isn't ours.
        _ownsRecording = !status.Value.Active;
        if (_ownsRecording && await Obs.StartRecordAsync() is { } refused) return Fail($"OBS wouldn't record: {refused}");

        _recordStartWall = DateTime.Now - (status.Value.Active ? status.Value.Elapsed : TimeSpan.Zero);
        _fromStream = fromStream;
        _recordOnly = fromStream && !SettingsService.Load().ClipWithStream;
        try { _facecam = await Obs.GetFacecamRectAsync(); } catch { _facecam = null; }
        try { _mics = (await Obs.GetAudioInputsAsync()).Mics; } catch { }

        lock (_gate)
        {
            _moments.Clear();
            _chat  = new ChatHypeDetector(Cooldown);
            _audio = new AudioPeakDetector(Cooldown);
            State  = ClipSessionState.Recording;
        }
        SetStatus(_recordOnly ? "Recording your stream" : fromStream ? "Clipping your stream" : "Clipping");
        return null;

        string Fail(string why)
        {
            lock (_gate) State = ClipSessionState.Idle;
            SetStatus("Not clipping");
            return why;
        }
    }

    /// <summary>Ends the session. Clips render once OBS reports the recording file is closed.</summary>
    public async Task StopSessionAsync()
    {
        if (State != ClipSessionState.Recording) return;
        if (_ownsRecording)
        {
            SetStatus("Stopping recording…");
            await Obs.StopRecordAsync();   // RecordStateChanged(STOPPED) carries the path → render
        }
        else
        {
            // Not our recording to stop: cut what we have once the streamer's recording ends.
            SetStatus("Waiting for your OBS recording to stop…");
        }
    }

    private async Task OnRecordingStoppedAsync(string? path)
    {
        List<ClipMoment> moments;
        TimeSpan length;
        lock (_gate)
        {
            if (State != ClipSessionState.Recording) return;   // a recording we weren't clipping
            State   = ClipSessionState.Rendering;
            moments = _moments.ToList();
            length  = DateTime.Now - _recordStartWall;
        }
        LastRecordingPath = path;

        try { await RenderSessionAsync(path, moments, length); }
        catch (Exception ex) { HotkeyCommands.Say("Clips failed", ex.Message, warn: true); SetStatus($"Clips failed: {ex.Message}"); }
        finally { lock (_gate) State = ClipSessionState.Idle; Changed?.Invoke(); }
    }

    private async Task RenderSessionAsync(string? path, List<ClipMoment> moments, TimeSpan length)
    {
        var s = SettingsService.Load();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            SetStatus("OBS didn't report where the recording was saved — no clips made.");
            return;
        }
        await Task.Delay(1500);   // let OBS release the file handle after the STOPPED event

        var segments = ClipPlanner.Plan(moments, TimeSpan.FromSeconds(s.ClipPreSeconds),
            TimeSpan.FromSeconds(s.ClipPostSeconds), length, s.MaxClipsPerStream);

        int made = 0;
        if (segments.Count > 0)
        {
            var ffmpeg = ClipRenderer.FindFfmpeg(s.FfmpegPath);
            if (ffmpeg is null)
            {
                // Keep the recording no matter what the save setting says — it's the only copy of these moments.
                SetStatus($"{segments.Count} highlight(s) marked, but ffmpeg wasn't found. Recording kept: {path}");
                HotkeyCommands.Say("Clips need ffmpeg", "Recording kept — see the Clips page.", warn: true);
                return;
            }

            var folder = Path.Combine(ClipsRoot(s), _recordStartWall.ToString("yyyy-MM-dd HH-mm"));
            Directory.CreateDirectory(folder);
            LastClipsFolder = folder;
            var layout = Enum.TryParse<ClipLayout>(s.ClipLayout, out var l) ? l : ClipLayout.BlurFill;

            for (int i = 0; i < segments.Count; i++)
            {
                SetStatus($"Rendering clip {i + 1} of {segments.Count}…");
                var name = $"clip-{i + 1:00} {Slug(segments[i].Reason)} {segments[i].Start:hh\\-mm\\-ss}.mp4";
                var (ok, error) = await ClipRenderer.RenderAsync(ffmpeg, path, Path.Combine(folder, name),
                    segments[i], layout, _facecam);
                if (ok) made++;
                else SetStatus($"Clip {i + 1} failed: {error}");
            }
        }

        // A partial failure keeps the recording so the missing clips can still be cut by hand.
        bool keep = s.SaveFullStream || !_ownsRecording || made < segments.Count;
        if (!keep) TryRecycle(path);

        var saved = keep && _ownsRecording ? $" Full stream saved: {Path.GetFileName(path)}" : "";
        SetStatus(segments.Count == 0 ? $"No highlights this time.{saved}" : $"{made} of {segments.Count} clip(s) ready.{saved}");
        if (made > 0) HotkeyCommands.Say($"{made} clip{(made == 1 ? "" : "s")} ready", "Open the Clips page to post them.");
    }

    private static void TryRecycle(string path)
    {
        // Recycle Bin, not delete: a wrong setting must stay recoverable.
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch { /* leaving the file is the safe failure */ }
    }

    // ── Marking highlights ───────────────────────────────────────────────────

    /// <summary>The "clip that" button/shortcut. Returns the toast to show.</summary>
    public async Task<(string Headline, string Detail, bool Warn)> ClipThatAsync()
    {
        if (State != ClipSessionState.Recording)
            return ("Not clipping right now", "Go live, or press Start clipping on the Clips page.", true);
        var at = await MarkAsync("you clipped it", 100, manual: true);
        return ("Clipped!", $"The last {SettingsService.Load().ClipPreSeconds}s at {at:hh\\:mm\\:ss}", false);
    }

    private async Task<TimeSpan> MarkAsync(string reason, double score, bool manual = false)
    {
        // OBS's own clock is authoritative (it pauses with the recording); wall clock is the fallback.
        var status = await Obs.GetRecordStatusAsync();
        var at = status is { Active: true } st ? st.Elapsed : DateTime.Now - _recordStartWall;
        lock (_gate)
        {
            if (State != ClipSessionState.Recording) return at;
            _moments.Add(new ClipMoment(at, reason, score, manual));
        }
        Changed?.Invoke();
        return at;
    }

    private void OnChat(TwitchChatMessage m)
    {
        if (State != ClipSessionState.Recording || m.IsLocalEcho || m.IsAlert) return;
        var s = SettingsService.Load();
        bool trusted = m.IsMod || m.IsVip || m.Username.Equals(s.TwitchUsername, StringComparison.OrdinalIgnoreCase);
        // !clip from a trusted user counts even with auto-detect off — it's a person asking.
        bool isCommand = trusted && m.Text.TrimStart().StartsWith("!clip", StringComparison.OrdinalIgnoreCase);
        if ((_recordOnly || !s.AutoClipEnabled) && !isCommand) return;

        string? reason;
        lock (_gate) reason = _chat.Add(DateTime.Now, m.Text, trusted);
        if (reason is not null) _ = MarkAsync(reason, isCommand ? 90 : 60, manual: isCommand);
    }

    private void OnAudioLevels(Dictionary<string, float> levels)
    {
        if (State != ClipSessionState.Recording || _mics.Length == 0) return;
        if (_recordOnly || !SettingsService.Load().AutoClipEnabled) return;

        float peak = 0;
        foreach (var mic in _mics)
            if (levels.TryGetValue(mic, out var v) && v > peak) peak = v;

        bool hit;
        lock (_gate) hit = _audio.Add(DateTime.Now, peak);
        if (hit) _ = MarkAsync("loud moment", 50);
    }

    private void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private static string Slug(string reason)
        => new string(reason.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace("  ", " ");
}
