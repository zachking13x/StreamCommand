#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StreamCommand.Dev;

/// <summary>
/// Debug-only TikTok clip (1080×1920, 30 fps, ~20 s). Story, not an ad: the alt-tab fail every
/// streamer knows → the fix (a shortcut, game keeps focus) → the dashboard → a quiet name drop.
/// No price, no "download now". Frames are drawn here and piped straight into ffmpeg.
/// </summary>
public static class TikTokVideo
{
    private const int W = 1080, H = 1920, Fps = 30;
    private const double Duration = 20.5;

    private static readonly FontFamily Font      = new("Bahnschrift, Segoe UI");
    private static readonly FontFamily Condensed = new("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI");
    private static readonly Color Teal  = Rgb("#00C9A7"), TealLight = Rgb("#4fe3c8"), Amber = Rgb("#F5B13D");
    private static readonly Color Navy  = Rgb("#0D1117"), Panel = Rgb("#0f1824"), Red = Rgb("#FF5A65");

    // Key-press beats (seconds) — shared by the visuals and the sound effects.
    private static readonly double[] SceneKeys = [7.8, 7.95, 8.1];
    private static readonly double[] MicKeys   = [10.8, 10.95, 11.1];
    private const double SceneToast = 8.3, MicToast = 11.3, Eliminated = 4.4, AltTabClick = 3.9;

    public static void Render(BitmapSource dashboard, string outDir)
    {
        // Render the game widescreen and crop the middle, like a real 16:9 capture on a phone;
        // no HUD so its minimap can't sit under the toasts.
        var game = StoreCapture.GameFrame((int)(H * 16 / 9.0), H, hud: false);
        var wav  = Path.Combine(outDir, "tiktok-sfx.wav");
        WriteSfx(wav);

        var mp4 = Path.Combine(outDir, "stream-command-tiktok.mp4");
        var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        var psi = new ProcessStartInfo(ffmpeg,
            $"-y -f rawvideo -pix_fmt bgra -s {W}x{H} -r {Fps} -i - -i \"{wav}\" " +
            "-c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -profile:v high -movflags +faststart " +
            $"-c:a aac -b:a 160k -shortest \"{mp4}\"")
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var log = proc.StandardError.ReadToEndAsync();
        var stdin = proc.StandardInput.BaseStream;
        var buf = new byte[W * H * 4];

        int frames = (int)(Duration * Fps);
        for (int f = 0; f < frames; f++)
        {
            double t = f / (double)Fps;
            var bmp = DrawFrame(t, game, dashboard);
            bmp.CopyPixels(buf, W * 4, 0);
            stdin.Write(buf, 0, buf.Length);
            if (Math.Abs(t - 1.6) < 0.5 / Fps) Save(bmp, Path.Combine(outDir, "tiktok-cover.png"));
        }
        stdin.Close();
        proc.WaitForExit();
        File.WriteAllText(Path.Combine(outDir, "ffmpeg-log.txt"), log.Result);
        if (proc.ExitCode != 0) throw new InvalidOperationException($"ffmpeg exited {proc.ExitCode} — see ffmpeg-log.txt");
    }

    // ── Story ────────────────────────────────────────────────────────────────

    private static BitmapSource DrawFrame(double t, BitmapSource game, BitmapSource dashboard)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Navy), null, new Rect(0, 0, W, H));

            if (t < 2.6)            Hook(dc, t, game);
            else if (t < Eliminated) AltTab(dc, t - 2.6);
            else if (t < 6.2)        Fail(dc, t - Eliminated, game);
            else if (t < 7.4)        Turn(dc, t - 6.2);
            else if (t < 13.0)       Fix(dc, t, game);
            else if (t < 17.5)       Reveal(dc, t - 13.0, dashboard);
            else                     Outro(dc, t - 17.5);

            // Fade from/to black at the very ends so loops feel clean.
            double fade = Math.Max(Smooth(1 - t / 0.25), Smooth((t - (Duration - 0.5)) / 0.5));
            if (fade > 0) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(fade * 255), 0, 0, 0)), null, new Rect(0, 0, W, H));
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    /// <summary>0–2.6 s: mid-match, the relatable setup.</summary>
    private static void Hook(DrawingContext dc, double t, BitmapSource game)
    {
        DrawGame(dc, game, 1.0 + 0.05 * t / 2.6, shake: 0);
        Caption(dc, "POV: you alt-tab to switch\nyour OBS scene mid-clutch", 430, Pop(t - 0.15));
    }

    /// <summary>2.6–4.4 s: the alt-tab scramble on the desktop.</summary>
    private static void AltTab(DrawingContext dc, double t)
    {
        dc.DrawRectangle(new SolidColorBrush(Rgb("#1b1e24")), null, new Rect(0, 0, W, H));
        // A generic scene switcher window (deliberately not any real app's UI)
        var win = new Rect(110, 640, 860, 720);
        dc.DrawRoundedRectangle(new SolidColorBrush(Rgb("#2a2d35")), new Pen(new SolidColorBrush(Rgb("#3a3e48")), 2), win, 14, 14);
        dc.DrawRoundedRectangle(new SolidColorBrush(Rgb("#22252c")), null, new Rect(win.X, win.Y, win.Width, 74), 14, 14);
        Label(dc, "Scenes", win.X + 34, win.Y + 18, 32, Rgb("#c9ccd4"), bold: true);

        string[] items = ["Starting Soon", "Just Chatting", "Gameplay", "Be Right Back", "Ending"];
        double clickAt = AltTabClick - 2.6;
        for (int i = 0; i < items.Length; i++)
        {
            var r = new Rect(win.X + 26, win.Y + 104 + i * 112, win.Width - 52, 92);
            bool picked = i == 2 && t >= clickAt;
            dc.DrawRoundedRectangle(new SolidColorBrush(picked ? Rgb("#3d5a9a") : Rgb("#33363f")), null, r, 10, 10);
            Label(dc, items[i], r.X + 30, r.Y + 24, 36, picked ? Colors.White : Rgb("#b8bcc6"));
        }

        // Cursor hunting for the right row, then clicking
        var from = new Point(860, 1560); var to = new Point(560, win.Y + 104 + 2 * 112 + 46);
        double k = EaseInOut(Math.Clamp(t / clickAt, 0, 1));
        var p = new Point(from.X + (to.X - from.X) * k + Math.Sin(t * 9) * 26 * (1 - k), from.Y + (to.Y - from.Y) * k);
        Cursor(dc, p, pressed: t >= clickAt && t < clickAt + 0.12);

        Caption(dc, "alt-tab. find the scene. click.", 380, Pop(t - 0.05), size: 64);
        // Quick alt-tab flash on the cut in
        if (t < 0.12) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(200 * (1 - t / 0.12)), 255, 255, 255)), null, new Rect(0, 0, W, H));
    }

    /// <summary>4.4–6.2 s: back in game, too late.</summary>
    private static void Fail(DrawingContext dc, double t, BitmapSource game)
    {
        double shake = t < 0.45 ? (0.45 - t) * 40 : 0;
        DrawGame(dc, game, 1.08, shake);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x88, 0x5a, 0x00, 0x08)), null, new Rect(0, 0, W, H));
        double s = 0.6 + 0.4 * EaseOutBack(Math.Clamp(t / 0.35, 0, 1));
        Outlined(dc, "ELIMINATED", W / 2.0, 860, 132 * s, Red, 10, center: true, font: Condensed);
        Caption(dc, "chat saw everything.", 1120, Pop(t - 0.5));
    }

    /// <summary>6.2–7.4 s: the turn.</summary>
    private static void Turn(DrawingContext dc, double t)
    {
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, W, H));
        const string line = "so I stopped alt-tabbing.";
        int n = (int)Math.Clamp(t / 0.55 * line.Length, 0, line.Length);
        Outlined(dc, line[..n], W / 2.0, 900, 70, Colors.White, 0, center: true);
    }

    /// <summary>7.4–13 s: the fix — shortcut, toast, game never loses focus.</summary>
    private static void Fix(DrawingContext dc, double t, BitmapSource game)
    {
        DrawGame(dc, game, 1.02 + 0.03 * (t - 7.4) / 5.6, 0);

        bool micPhase = t >= 10.6;
        var keys  = micPhase ? new[] { "Ctrl", "Shift", "F10" } : new[] { "Ctrl", "Shift", "F2" };
        var beats = micPhase ? MicKeys : SceneKeys;
        double appear = micPhase ? Smooth((t - 10.6) / 0.15) : Smooth((t - 7.45) / 0.2);
        Keycaps(dc, keys, beats, t, 1240, appear);

        if (t >= SceneToast && t < 10.6)
            Toast(dc, "Gameplay", "Now on program", Teal, Smooth((t - SceneToast) / 0.22), Smooth((10.5 - t) / 0.2));
        if (t >= MicToast)
            Toast(dc, "Mic muted", "Ctrl + Shift + F10", Amber, Smooth((t - MicToast) / 0.22), Smooth((13.0 - t) / 0.25));

        if (t >= 8.6 && t < 10.5) Caption(dc, "game never leaves focus.", 640, Pop(t - 8.6), size: 64);
        if (t >= 11.5)            Caption(dc, "mic too.", 640, Pop(t - 11.5), size: 64);
    }

    /// <summary>13–17.5 s: the second monitor.</summary>
    private static void Reveal(DrawingContext dc, double t, BitmapSource dashboard)
    {
        dc.DrawRectangle(new SolidColorBrush(Rgb("#070b11")), null, new Rect(0, 0, W, H));
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0x55, Teal.R, Teal.G, Teal.B), Color.FromArgb(0, Teal.R, Teal.G, Teal.B)), null, new Point(W / 2.0, 980), 700, 520);

        // Monitor frame with the real dashboard; slow push-in toward the stats + scenes.
        double z = 1 + 1.35 * EaseInOut(Math.Clamp((t - 0.9) / 3.4, 0, 1));
        var screen = new Rect(40, 720, W - 80, (W - 80) * 9.0 / 16);
        dc.DrawRoundedRectangle(new SolidColorBrush(Rgb("#0b1018")), new Pen(new SolidColorBrush(Rgb("#2a4a5a")), 3), Inflate(screen, 14), 22, 22);
        dc.PushClip(new RectangleGeometry(screen, 10, 10));
        double vw = screen.Width * z, vh = screen.Height * z;
        double fx = 0.30, fy = 0.12;   // focus point on the dashboard (stats strip / scene control)
        dc.DrawImage(dashboard, new Rect(screen.X - (vw - screen.Width) * fx, screen.Y - (vh - screen.Height) * fy, vw, vh));
        dc.Pop();
        // Stand
        dc.DrawRectangle(new SolidColorBrush(Rgb("#121a24")), null, new Rect(W / 2.0 - 70, screen.Bottom + 14, 140, 90));
        dc.DrawRoundedRectangle(new SolidColorBrush(Rgb("#121a24")), null, new Rect(W / 2.0 - 220, screen.Bottom + 100, 440, 22), 11, 11);

        Caption(dc, "everything else lives on\nmy second monitor", 420, Pop(t - 0.15));
    }

    /// <summary>17.5 s → end: the quiet name drop. No price, no call to action.</summary>
    private static void Outro(DrawingContext dc, double t)
    {
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, W, H));
        double a = Smooth(t / 0.4);
        // Logo mark
        var tile = new Rect(W / 2.0 - 64, 760, 128, 128);
        dc.PushOpacity(a);
        dc.DrawRoundedRectangle(new LinearGradientBrush(Rgb("#1fcfb1"), Rgb("#00b394"), 90), null, tile, 34, 34);
        var pen = new Pen(new SolidColorBrush(Rgb("#0a0e14")), 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var c = new Point(tile.X + 64, tile.Y + 64);
        dc.DrawEllipse(new SolidColorBrush(Rgb("#0a0e14")), null, c, 9, 9);
        foreach (var r in new[] { 26.0, 46.0 })
        {
            dc.DrawGeometry(null, pen, Arc(c, r, 135, 225));
            dc.DrawGeometry(null, pen, Arc(c, r, -45, 45));
        }
        Outlined(dc, "(it's called Stream Command)", W / 2.0, 950, 54, Colors.White, 0, center: true);
        Outlined(dc, "for Windows + OBS", W / 2.0, 1030, 38, TealLight, 0, center: true, font: Condensed);
        dc.Pop();
    }

    // ── Drawing pieces ───────────────────────────────────────────────────────

    private static void DrawGame(DrawingContext dc, BitmapSource game, double zoom, double shake)
    {
        double w = game.PixelWidth * zoom, h = game.PixelHeight * zoom;
        double sx = shake * Math.Sin(shake * 7.3), sy = shake * Math.Cos(shake * 5.1);
        dc.DrawImage(game, new Rect((W - w) / 2 + sx, (H - h) / 2 + sy, w, h));
    }

    private static void Keycaps(DrawingContext dc, string[] keys, double[] beats, double t, double y, double appear)
    {
        if (appear <= 0) return;
        dc.PushOpacity(appear);
        double gap = 26, h = 150;
        var widths = keys.Select(k => k.Length > 3 ? 250.0 : 190.0).ToArray();
        double x = (W - widths.Sum() - gap * (keys.Length - 1)) / 2;
        for (int i = 0; i < keys.Length; i++)
        {
            bool down = t >= beats[i];
            double press = down ? 10 : 0;
            var body = new Rect(x, y + 14, widths[i], h);
            var top  = new Rect(x, y + press, widths[i], h);
            dc.DrawRoundedRectangle(new SolidColorBrush(down ? Rgb("#007a66") : Rgb("#0a3a33")), null, body, 22, 22);
            dc.DrawRoundedRectangle(new SolidColorBrush(down ? Rgb("#0f2a2a") : Rgb("#0b1420")),
                                    new Pen(new SolidColorBrush(down ? TealLight : Teal), 4), top, 22, 22);
            Outlined(dc, keys[i], x + widths[i] / 2, top.Y + 40, 60, Colors.White, 0, center: true, font: Condensed);
            x += widths[i] + gap;
        }
        dc.Pop();
    }

    private static void Toast(DrawingContext dc, string head, string detail, Color accent, double inT, double outT)
    {
        double a = Math.Min(inT, outT);
        if (a <= 0) return;
        double y = 140 + 60 * (1 - EaseOutBack(Math.Clamp(inT, 0, 1)));
        var r = new Rect(110, y, W - 220, 190);
        dc.PushOpacity(a);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), null, new Rect(r.X + 6, r.Y + 14, r.Width, r.Height), 30, 30);
        dc.DrawRoundedRectangle(new SolidColorBrush(Panel), new Pen(new SolidColorBrush(accent), 4), r, 30, 30);
        var tile = new Rect(r.X + 36, r.Y + 41, 108, 108);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), null, tile, 24, 24);
        dc.DrawEllipse(new SolidColorBrush(accent), null, new Point(tile.X + 54, tile.Y + 54), 20, 20);
        Label(dc, head,   r.X + 176, r.Y + 34, 60, Colors.White, bold: true);
        Label(dc, detail, r.X + 178, r.Y + 112, 36, Rgb("#8a9db3"));
        dc.Pop();
    }

    private static void Cursor(DrawingContext dc, Point p, bool pressed)
    {
        var g = Geometry.Parse("M0,0 L0,58 L15,44 L25,68 L35,64 L25,40 L44,40 Z");
        dc.PushTransform(new TranslateTransform(p.X, p.Y));
        dc.PushTransform(new ScaleTransform(pressed ? 0.88 : 1, pressed ? 0.88 : 1));
        dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 3), g);
        dc.Pop(); dc.Pop();
    }

    /// <summary>TikTok-native caption: heavy white text, black outline, popped in.</summary>
    private static void Caption(DrawingContext dc, string text, double y, double pop, double size = 72)
    {
        if (pop <= 0) return;
        double s = 0.85 + 0.15 * EaseOutBack(Math.Clamp(pop, 0, 1));
        dc.PushOpacity(Math.Clamp(pop * 3, 0, 1));
        dc.PushTransform(new ScaleTransform(s, s, W / 2.0, y + size));
        Outlined(dc, text, W / 2.0, y, size, Colors.White, 12, center: true);
        dc.Pop(); dc.Pop();
    }

    private static void Outlined(DrawingContext dc, string text, double x, double y, double size, Color fill, double stroke,
                                 bool center = false, FontFamily? font = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(font ?? Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, Brushes.White, 1.0)
        { TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, MaxTextWidth = W - 120 };
        double ox = center ? x - (W - 120) / 2.0 : x;
        var geo = ft.BuildGeometry(new Point(ox, y));
        if (stroke > 0)
            dc.DrawGeometry(null, new Pen(Brushes.Black, stroke) { LineJoin = PenLineJoin.Round }, geo);
        dc.DrawGeometry(new SolidColorBrush(fill), null, geo);
    }

    private static void Label(DrawingContext dc, string text, double x, double y, double size, Color color, bool bold = false)
        => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               new Typeface(Font, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.SemiBold, FontStretches.Normal),
               size, new SolidColorBrush(color), 1.0), new Point(x, y));

    private static Geometry Arc(Point c, double r, double fromDeg, double toDeg)
    {
        Point P(double d) => new(c.X + r * Math.Cos(d * Math.PI / 180), c.Y + r * Math.Sin(d * Math.PI / 180));
        var fig = new PathFigure { StartPoint = P(fromDeg) };
        fig.Segments.Add(new ArcSegment(P(toDeg), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        return new PathGeometry([fig]);
    }

    // ── Sound: generated SFX, timed to the same beats ────────────────────────

    private static void WriteSfx(string path)
    {
        const int rate = 44100;
        var s = new float[(int)(Duration * rate)];
        var rng = new Random(7);

        void Click(double at)   // mechanical key
        {
            int start = (int)(at * rate);
            for (int i = 0; i < rate * 0.035 && start + i < s.Length; i++)
                s[start + i] += (float)((rng.NextDouble() * 2 - 1) * Math.Exp(-i / (rate * 0.006)) * 0.55
                                        + Math.Sin(2 * Math.PI * 1800 * i / rate) * Math.Exp(-i / (rate * 0.004)) * 0.25);
        }
        void Blip(double at, double f0, double f1, double vol)   // toast
        {
            int start = (int)(at * rate), n = (int)(rate * 0.16);
            double phase = 0;
            for (int i = 0; i < n && start + i < s.Length; i++)
            {
                double f = f0 + (f1 - f0) * i / n;
                phase += 2 * Math.PI * f / rate;
                s[start + i] += (float)(Math.Sin(phase) * vol * Math.Sin(Math.PI * i / n));
            }
        }
        void Thump(double at)   // elimination hit
        {
            int start = (int)(at * rate);
            for (int i = 0; i < rate * 0.6 && start + i < s.Length; i++)
            {
                double env = Math.Exp(-i / (rate * 0.14));
                s[start + i] += (float)((Math.Sin(2 * Math.PI * (55 - 25.0 * i / (rate * 0.6)) * i / rate) * 0.9
                                        + (rng.NextDouble() * 2 - 1) * 0.25 * Math.Exp(-i / (rate * 0.03))) * env);
            }
        }
        void Whoosh(double at, double len)   // cuts
        {
            int start = (int)(at * rate), n = (int)(rate * len);
            double lp = 0;
            for (int i = 0; i < n && start + i < s.Length; i++)
            {
                lp += 0.08 * ((rng.NextDouble() * 2 - 1) - lp);
                s[start + i] += (float)(lp * 1.6 * Math.Sin(Math.PI * i / n));
            }
        }

        Whoosh(2.5, 0.25);                          // alt-tab
        Click(2.62); Click(2.70);                   // alt + tab
        Click(AltTabClick);                         // mouse click
        Thump(Eliminated);
        Whoosh(7.3, 0.3);
        foreach (var b in SceneKeys) Click(b);
        Blip(SceneToast, 660, 990, 0.35);
        foreach (var b in MicKeys) Click(b);
        Blip(MicToast, 520, 390, 0.35);
        Whoosh(12.95, 0.35);
        Blip(17.6, 440, 660, 0.18);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        int bytes = s.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(bytes);
        foreach (var v in s) w.Write((short)(Math.Clamp(v, -1f, 1f) * 32000));
    }

    // ── Easing / utils ───────────────────────────────────────────────────────

    private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    private static double EaseInOut(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    private static double EaseOutBack(double x) { const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }
    private static double Pop(double since) => since < 0 ? 0 : Math.Clamp(since / 0.25, 0, 1);
    private static Rect Inflate(Rect r, double d) { r.Inflate(d, d); return r; }
    private static Color Rgb(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void Save(BitmapSource bmp, string file)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
#endif
