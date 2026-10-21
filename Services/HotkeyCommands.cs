namespace StreamCommand.Services;

/// <summary>
/// What each global shortcut does. Kept apart from the Win32 plumbing so the stream-safety
/// rules (double-press to go live/end, nothing happens without OBS) live in one place.
/// </summary>
public static class HotkeyCommands
{
    /// <summary>Feedback for the on-screen toast: (headline, detail, isWarning).</summary>
    public static event Action<string, string, bool>? Feedback;

    private static readonly DoublePressGuard _streamGuard = new(TimeSpan.FromSeconds(3));
    private static OBSWebSocketService Obs => OBSWebSocketService.Shared;

    public static async Task ExecuteAsync(HotkeyBinding b)
    {
        if (Obs.State != OBSState.Connected)
        {
            _streamGuard.Reset();
            Say("OBS isn't connected", "Open Stream Command and connect OBS first.", warn: true);
            return;
        }

        switch (b.Action)
        {
            case HotkeyAction.SwitchScene:
                var scenes = await Obs.GetScenesAsync();
                if (b.SceneIndex >= scenes.Length)
                {
                    Say($"No scene {b.SceneIndex + 1}", $"OBS has {scenes.Length} scene{(scenes.Length == 1 ? "" : "s")}.", warn: true);
                    return;
                }
                await Obs.SetSceneAsync(scenes[b.SceneIndex]);
                Say(scenes[b.SceneIndex], "Now on program");
                break;

            case HotkeyAction.ToggleMic:
                var (mics, _) = await Obs.GetAudioInputsAsync();
                if (mics.Length == 0) { Say("No mic found in OBS", "Add a mic source in OBS first.", warn: true); return; }
                var muted = await Obs.ToggleInputMuteAsync(mics[0]);
                if (muted is null) { Say("OBS didn't respond", "Mic state unchanged.", warn: true); return; }
                Say(muted.Value ? "Mic muted" : "Mic live", mics[0], warn: muted.Value);
                break;

            case HotkeyAction.ToggleStream:
                var verb = Obs.IsStreaming ? "end the stream" : "go live";
                if (!_streamGuard.Press(DateTime.Now))
                {
                    Say($"Press {b.Label} again to {verb}", "Within 3 seconds — or ignore to cancel.", warn: true);
                    return;
                }
                if (Obs.IsStreaming) { await Obs.StopStreamAsync(); Say("Ending stream…", "Great stream!"); }
                else                 { await Obs.StartStreamAsync(); Say("Going live…", "OBS is starting your stream."); }
                break;
        }
    }

    private static void Say(string headline, string detail, bool warn = false) => Feedback?.Invoke(headline, detail, warn);
}
