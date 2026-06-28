namespace StreamCommand.Services;

/// <summary>
/// Lightweight static event bus for cross-component stream events.
/// Any view can raise an alert; the overlay and other views subscribe here.
/// </summary>
public static class StreamEvents
{
    /// <summary>Fired when the stream goes live or ends. isLive = true means just went live.</summary>
    public static event Action<bool>? StreamStateChanged;

    /// <summary>Fired for any notable chat event (sub, raid, follow, etc.).</summary>
    public static event Action<string, string>? AlertFired;   // (emoji, message)

    /// <summary>Fired when OBS WebSocket connects or disconnects. isConnected = true means just connected.</summary>
    public static event Action<bool>? OBSStateChanged;

    /// <summary>Fired when the pre-stream checklist progress changes. (done, total)</summary>
    public static event Action<int, int>? ChecklistProgressChanged;

    /// <summary>Fired whenever Planner events are saved — Dashboard subscribes to refresh its upcoming list.</summary>
    public static event Action? PlannerChanged;

    /// <summary>
    /// Fired whenever a usage counter changes (streams, OBS sessions, automation, Pro gate hits).
    /// DashboardView subscribes to this to evaluate the conversion card trigger.
    /// </summary>
    public static event Action? UsageUpdated;

    public static void RaiseStreamState(bool isLive) => StreamStateChanged?.Invoke(isLive);
    public static void RaiseAlert(string emoji, string message) => AlertFired?.Invoke(emoji, message);
    public static void RaiseOBSState(bool isConnected) => OBSStateChanged?.Invoke(isConnected);
    public static void RaiseChecklistProgress(int done, int total) => ChecklistProgressChanged?.Invoke(done, total);
    public static void RaisePlannerChanged() => PlannerChanged?.Invoke();
    public static void RaiseUsageUpdated() => UsageUpdated?.Invoke();
}
