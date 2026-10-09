using System;
using System.Collections.Generic;

namespace StreamCommand.Services;

// Pure, socket-free pieces of the Twitch chat client, split out of TwitchChatService so
// the protocol logic can be unit tested without a live connection.

/// <summary>Minimal IRC message split: tags, prefix, command, params, trailing.</summary>
public readonly record struct IrcLine(string Tags, string Prefix, string Command, string Parameters, string Trailing);

/// <summary>The Twitch IRCv3 tags this app reads, with their documented defaults.</summary>
public sealed record TwitchTags
{
    public string Color       { get; init; } = "#C084FC";
    public bool   IsSub       { get; init; }
    public bool   IsMod       { get; init; }
    public bool   IsVip       { get; init; }
    public string MsgId       { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Months      { get; init; } = "1";
    public string Recipient   { get; init; } = "";
    public string GiftCount   { get; init; } = "1";
    public string ViewerCount { get; init; } = "0";
    public string Id          { get; init; } = "";
}

public static class IrcParser
{
    public static IrcLine ParseLine(string line)
    {
        string tags = "", prefix = "";
        var rest = line;

        if (rest.StartsWith('@'))
        {
            int sp = rest.IndexOf(' ');
            if (sp < 0) return new IrcLine(rest[1..], "", "", "", "");
            tags = rest[1..sp];
            rest = rest[(sp + 1)..];
        }

        if (rest.StartsWith(':'))
        {
            int sp = rest.IndexOf(' ');
            if (sp < 0) return new IrcLine(tags, rest[1..], "", "", "");
            prefix = rest[1..sp];
            rest = rest[(sp + 1)..];
        }

        // Trailing parameter begins at the first " :" — its content is preserved verbatim.
        string trailing = "";
        int trailIdx = rest.IndexOf(" :", StringComparison.Ordinal);
        if (trailIdx >= 0)
        {
            trailing = rest[(trailIdx + 2)..];
            rest     = rest[..trailIdx];
        }

        int cmdEnd  = rest.IndexOf(' ');
        var command = cmdEnd < 0 ? rest : rest[..cmdEnd];
        var pars    = cmdEnd < 0 ? ""   : rest[(cmdEnd + 1)..];

        return new IrcLine(tags, prefix, command.Trim(), pars.Trim(), trailing);
    }

    public static TwitchTags ParseTags(string tags)
    {
        var t = new TwitchTags();
        if (string.IsNullOrEmpty(tags)) return t;

        foreach (var tag in tags.Split(';'))
        {
            var eq = tag.IndexOf('=');
            if (eq < 0) continue;
            var key = tag[..eq];
            var val = tag[(eq + 1)..];

            t = key switch
            {
                "color" when !string.IsNullOrEmpty(val)  => t with { Color = val },
                "subscriber"                             => t with { IsSub = val == "1" },
                "mod"                                    => t with { IsMod = val == "1" },
                "vip"                                    => t with { IsVip = val == "1" },
                "msg-id"                                 => t with { MsgId = val },
                "display-name"                           => t with { DisplayName = val },
                "id"                                     => t with { Id = val },
                "msg-param-months"                       => t with { Months = val },
                "msg-param-recipient-display-name"       => t with { Recipient = val },
                "msg-param-mass-gift-count"              => t with { GiftCount = val },
                "msg-param-viewerCount"                  => t with { ViewerCount = val },
                _                                        => t,
            };
        }
        return t;
    }

    public static TwitchChatMessage? ParsePrivmsg(IrcLine irc)
    {
        var excl     = irc.Prefix.IndexOf('!');
        var username = excl > 0 ? irc.Prefix[..excl] : irc.Prefix;
        if (string.IsNullOrEmpty(username)) return null;

        var t = ParseTags(irc.Tags);
        return new TwitchChatMessage
        {
            Username  = string.IsNullOrEmpty(t.DisplayName) ? username : t.DisplayName,
            Color     = t.Color,
            Text      = irc.Trailing,
            IsSub     = t.IsSub,
            IsMod     = t.IsMod,
            IsVip     = t.IsVip,
            EventType = TwitchEventType.ChatMessage,
            Time      = DateTime.Now
        };
    }

    public static TwitchChatMessage? ParseUserNotice(string tags)
    {
        var t = ParseTags(tags);

        (TwitchEventType type, string text)? mapped = t.MsgId switch
        {
            "sub"            => (TwitchEventType.Subscribe, $"⭐ {t.DisplayName} just subscribed!"),
            "resub"          => (TwitchEventType.Resub,     $"⭐ {t.DisplayName} resubscribed for {t.Months} months!"),
            "subgift"        => (TwitchEventType.GiftSub,   $"🎁 {t.DisplayName} gifted a sub to {t.Recipient}!"),
            "submysterygift" => (TwitchEventType.GiftSub,   $"🎁 {t.DisplayName} is gifting {t.GiftCount} subs!"),
            "raid"           => (TwitchEventType.Raid,      $"🚀 {t.DisplayName} is raiding with {t.ViewerCount} viewers!"),
            _                => null,
        };
        if (mapped is not { } m) return null;

        return new TwitchChatMessage
        {
            Username    = t.DisplayName,
            Color       = "#F59E0B",
            Text        = m.text,
            IsAlert     = true,
            EventType   = m.type,
            Months      = t.Months,
            RaiderName  = t.DisplayName,
            ViewerCount = t.ViewerCount,
            Time        = DateTime.Now
        };
    }
}

/// <summary>
/// Sliding-window limiter for outbound PRIVMSG. Twitch allows ~20 messages / 30s for a
/// non-moderator; going over gets the account silently dropped for 30 minutes.
/// </summary>
public sealed class ChatRateLimiter(int burst = 18, TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(30);
    private readonly Queue<DateTime> _sent = new();

    /// <summary>
    /// Claims a send slot and returns <see cref="TimeSpan.Zero"/>, or claims nothing and
    /// returns how long until one frees up. Never blocks, so the writer can keep serving
    /// PONGs while chat is throttled.
    /// </summary>
    public TimeSpan TryReserve(DateTime nowUtc)
    {
        lock (_sent)
        {
            while (_sent.Count > 0 && nowUtc - _sent.Peek() >= _window)
                _sent.Dequeue();

            if (_sent.Count < burst)
            {
                _sent.Enqueue(nowUtc);
                return TimeSpan.Zero;
            }
            return _window - (nowUtc - _sent.Peek());
        }
    }
}

/// <summary>
/// Drops events already seen by their `id` tag. Twitch may redeliver, and a reconnect can
/// replay recent events. Automation has side effects (it posts to chat), so a replay must
/// not fire it twice.
/// </summary>
public sealed class MessageDeduplicator(TimeSpan? window = null, int maxIds = 1000)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(5);
    private readonly HashSet<string> _seen = new();
    private readonly Queue<(string Id, DateTime T)> _order = new();

    public bool IsDuplicate(string id, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(id)) return false;   // no id → cannot dedup, let it through

        lock (_seen)
        {
            while (_order.Count > 0 && (nowUtc - _order.Peek().T > _window || _order.Count >= maxIds))
                _seen.Remove(_order.Dequeue().Id);

            if (!_seen.Add(id)) return true;
            _order.Enqueue((id, nowUtc));
            return false;
        }
    }
}
