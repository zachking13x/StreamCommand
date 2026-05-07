using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace StreamCommand.Services;

public static class AppLaunchService
{
    private static readonly Dictionary<string, string[]> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["obs"] =
        [
            @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
            @"C:\Program Files (x86)\obs-studio\bin\64bit\obs64.exe"
        ],
        ["streamlabs"] =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"streamlabs-obs\streamlabs-obs.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"streamlabs-obs\streamlabs-obs.exe")
        ],
        ["twitch-studio"] =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\TwitchStudio\TwitchStudio.exe")
        ],
        ["xsplit"] =
        [
            @"C:\Program Files\SplitmediaLabs\XSplit Broadcaster\XSplit.Core.exe"
        ]
    };

    public static (bool success, string message) TryLaunch(string appKey)
    {
        if (!KnownApps.TryGetValue(appKey, out var paths))
            return (false, "Unknown application.");

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        return (false, $"Could not find the app on your PC. Please launch it manually from your desktop or taskbar.");
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* swallow — can't open browser */ }
    }
}
