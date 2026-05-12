using System.ComponentModel;

namespace StreamCommand.Models;

public enum AutomationTrigger
{
    NewSubscriber,  // USERNOTICE msg-id=sub
    Resub,          // USERNOTICE msg-id=resub
    GiftSub,        // USERNOTICE msg-id=subgift / submysterygift
    Raid,           // USERNOTICE msg-id=raid
    ChatCommand,    // PRIVMSG starting with a specific !keyword
    NewFollower,    // EventSub channel.follow (v2) — requires moderator:read:followers scope
}

public class AutomationRule : INotifyPropertyChanged
{
    private bool _isEnabled;

    // ── Identity ─────────────────────────────────────────────────────────────
    public string Id       { get; set; } = System.Guid.NewGuid().ToString();
    public string Category { get; set; } = "";

    // ── Display (used by the existing XAML DataTemplate) ─────────────────────
    public string Trigger { get; set; } = "";   // human-readable trigger label
    public string Action  { get; set; } = "";   // human-readable action label

    // ── Typed trigger ─────────────────────────────────────────────────────────
    public AutomationTrigger TriggerType    { get; set; } = AutomationTrigger.ChatCommand;

    /// <summary>Only used when TriggerType == ChatCommand (e.g. "!socials").</summary>
    public string CommandKeyword            { get; set; } = "";

    // ── Action ────────────────────────────────────────────────────────────────
    /// <summary>
    /// The message to send to chat. Supports placeholders:
    ///   @user     → viewer's display name
    ///   @raider   → raiding streamer's name
    ///   {months}  → months count on resub
    /// </summary>
    public string ResponseTemplate          { get; set; } = "";

    // ── Toggle ────────────────────────────────────────────────────────────────
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
