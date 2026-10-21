using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace StreamCommand.Services;

/// <summary>
/// Cuts a window out of a recording and renders it as a 1080×1920 vertical short with ffmpeg.
/// ffmpeg is not bundled (licensing + ~100 MB); the user's own install is found or pointed at.
/// </summary>
public static class ClipRenderer
{
    public const int OutW = 1080, OutH = 1920;
    private const int CamH = 640;   // facecam band on top; gameplay gets the remaining 1280

    /// <summary>Finds ffmpeg: the configured path, then PATH, then the winget shim folder (not on PATH for packaged apps).</summary>
    public static string? FindFfmpeg(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"))
            .Append(@"C:\ffmpeg\bin")
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"));

        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var exe = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(exe)) return exe;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>The ffmpeg filter graph producing [v] at 1080×1920. Pure, so the layouts are testable.</summary>
    public static string BuildFilter(ClipLayout layout, NormalizedRect? facecam)
    {
        if (layout == ClipLayout.FacecamSplit && facecam is not null)
        {
            string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            var gameH = OutH - CamH;
            // Gameplay: centre slice at the band's aspect. Facecam: its canvas rectangle, filled to the band.
            return "[0:v]split=2[cam][game];" +
                   $"[cam]crop=trunc(iw*{F(facecam.W)}/2)*2:trunc(ih*{F(facecam.H)}/2)*2:iw*{F(facecam.X)}:ih*{F(facecam.Y)}," +
                   $"scale={OutW}:{CamH}:force_original_aspect_ratio=increase,crop={OutW}:{CamH}[top];" +
                   $"[game]crop=trunc(ih*{OutW}/{gameH}/2)*2:ih,scale={OutW}:{gameH}[bottom];" +
                   "[top][bottom]vstack=inputs=2,setsar=1,format=yuv420p[v]";
        }

        // No facecam to lift: a 4:3 centre slice of the game over a blurred, zoomed copy of itself —
        // the HUD edges are lost but the action stays big on a phone.
        return "[0:v]split=2[bg][fg];" +
               $"[bg]scale={OutW}:{OutH}:force_original_aspect_ratio=increase,crop={OutW}:{OutH},boxblur=24:2,eq=brightness=-0.12[blur];" +
               $"[fg]crop=trunc(ih*4/3/2)*2:ih,scale={OutW}:-2[mid];" +
               "[blur][mid]overlay=(W-w)/2:(H-h)/2,setsar=1,format=yuv420p[v]";
    }

    public static string BuildArgs(string input, string output, ClipSegment seg, string filter, bool nvenc)
    {
        string S(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        // -ss before -i seeks by keyframe then decodes forward, which is frame-accurate when re-encoding.
        var video = nvenc ? "-c:v h264_nvenc -preset p5 -rc vbr -cq 21 -b:v 0" : "-c:v libx264 -preset veryfast -crf 20";
        return $"-hide_banner -y -ss {S(seg.Start)} -i \"{input}\" -t {S(seg.Duration)} " +
               $"-filter_complex \"{filter}\" -map \"[v]\" -map 0:a:0? {video} -r 60 " +
               $"-c:a aac -b:a 160k -movflags +faststart \"{output}\"";
    }

    /// <summary>Renders one clip; tries the GPU encoder first and falls back to x264 if it fails.</summary>
    public static async Task<(bool Ok, string Error)> RenderAsync(string ffmpeg, string input, string output,
        ClipSegment seg, ClipLayout layout, NormalizedRect? facecam, CancellationToken ct = default)
    {
        var filter = BuildFilter(layout, facecam);
        var (ok, err) = await RunAsync(ffmpeg, BuildArgs(input, output, seg, filter, nvenc: true), ct);
        if (ok) return (true, "");
        // NVENC is listed by every ffmpeg build but needs an NVIDIA GPU; a failure there is
        // routine on AMD/Intel machines, so retry on the CPU before reporting anything.
        var (cpuOk, cpuErr) = await RunAsync(ffmpeg, BuildArgs(input, output, seg, filter, nvenc: false), ct);
        return cpuOk ? (true, "") : (false, LastLines(cpuErr.Length > 0 ? cpuErr : err));
    }

    private static async Task<(bool Ok, string Error)> RunAsync(string ffmpeg, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) return (false, "ffmpeg could not be started.");
        // Rendering must never steal frames from a game that is still running.
        try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        var stderr = proc.StandardError.ReadToEndAsync(ct);
        _ = proc.StandardOutput.ReadToEndAsync(ct);
        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } throw; }
        return (proc.ExitCode == 0, await stderr);
    }

    private static string LastLines(string text)
        => string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(2)).Trim();
}
