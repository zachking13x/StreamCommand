using System;

namespace StreamCommand.Models;

public class StreamEvent
{
    public string Title { get; set; } = "";
    public string Platform { get; set; } = "Twitch";
    public DateTime StreamDateTime { get; set; } = DateTime.Now.AddDays(1);
    public string Duration { get; set; } = "2h";
    public string Notes { get; set; } = "";

    public string DateDisplay => StreamDateTime.ToString("MMM d");
    public string DayDisplay => StreamDateTime.ToString("ddd").ToUpper();
    public string DayNumber => StreamDateTime.Day.ToString();
    public string TimeDisplay => StreamDateTime.ToString("h:mm tt");
    /// <summary>True when the stream date has already passed.</summary>
    public bool IsPast => StreamDateTime < DateTime.Now;

    /// <summary>Lower opacity for past events so they don't dominate the view.</summary>
    public double CardOpacity => IsPast ? 0.45 : 1.0;

    /// <summary>Strike-through label appended to past events.</summary>
    public string PastLabel => IsPast ? "  (past)" : "";

    public string PlatformBadgeColor => Platform switch
    {
        "YouTube" => "#7F1D1D",
        "Both" => "#1E3A5F",
        _ => "#2E1065"
    };
    public string PlatformTextColor => Platform switch
    {
        "YouTube" => "#FCA5A5",
        "Both" => "#93C5FD",
        _ => "#C4B5FD"
    };

}
