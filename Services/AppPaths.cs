using System.IO;

namespace StreamCommand.Services;

/// <summary>Where settings.json / subscription.json live.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Resolve();

    private static string Resolve()
    {
#if DEBUG
        // Debug-only sandbox (Store screenshot capture, testing) so a dev run never reads or
        // writes the real profile. Compiled out of Release: shipped builds can't be redirected.
        var overrideDir = Environment.GetEnvironmentVariable("STREAMCMD_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamCommand");
    }
}
