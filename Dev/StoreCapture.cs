#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using StreamCommand.Services;
using StreamCommand.Views;

namespace StreamCommand.Dev;

/// <summary>
/// Debug-only Microsoft Store asset generator. Run the Debug build with
/// <c>STREAMCMD_DATA_DIR=&lt;empty sandbox dir&gt;</c> and <c>--store-capture &lt;out dir&gt;</c>: it seeds a demo
/// profile, drives every page, renders each at exactly 1920×1080 and writes two marketing mockups.
/// The sandbox env var keeps the real profile (account, chat, OBS password) out of every shot.
/// Not compiled into Release.
/// </summary>
public static class StoreCapture
{
    public const int W = 1920, H = 1080;

    public static bool Active { get; private set; }
    private static string _outDir = "";
    private static bool _tiktok;   // --tiktok: render only the dashboard, then the TikTok clip (Dev/TikTokVideo.cs)

    /// <summary>Parses the args; true when a capture run was requested (and the sandbox is set).</summary>
    public static bool TryBegin(string[] args)
    {
        var i = Array.IndexOf(args, "--store-capture");
        if (i < 0 || i + 1 >= args.Length) return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STREAMCMD_DATA_DIR")))
            throw new InvalidOperationException("Refusing to capture against the real profile — set STREAMCMD_DATA_DIR to a sandbox folder.");

        _outDir = args[i + 1];
        _tiktok = args.Contains("--tiktok");
        Directory.CreateDirectory(_outDir);
        Active = true;
        SeedDemoProfile();
        return true;
    }

    private static void SeedDemoProfile()
    {
        var s = SettingsService.Load();
        s.SetupComplete            = true;
        s.TwitchUsername           = "novaplays";
        s.DevProUnlock             = true;          // Debug-only flag: shows the Pro experience
        s.AccentColor              = ThemeService.DefaultAccent;
        s.GlobalHotkeysEnabled     = true;
        s.HotkeyModifier           = nameof(HotkeyModifierSet.CtrlShift);
        s.StreamsCompleted         = 12;
        s.FirstLaunchDate          = DateTime.Now.AddDays(-30);
        s.ReviewPromptShown        = true;
        s.StreamValueCardDismissed = true;
        SettingsService.Save(s);
    }

    public static async Task RunAsync(MainWindow window)
    {
        try
        {
            ThemeService.Apply(ThemeService.DefaultAccent);
            var root = (FrameworkElement)window.Content;
            root.IsHitTestVisible = false;   // no stray hover states from wherever the real cursor sits
            window.ApplyStoreDemo();
            window.Width = W + 40; window.Height = H + 60;
            root.Width = W; root.Height = H;

            await Settle(1500);

            var shots = new (string Tag, string File)[]
            {
                ("dashboard",    "01-dashboard.png"),
                ("live-control", "02-live-control.png"),
                ("pre-stream",   "04-pre-stream-checklist.png"),
                ("automation",   "05-automation.png"),
                ("chat-monitor", "06-chat-monitor.png"),
                ("analytics",    "07-analytics.png"),
                ("planner",      "08-planner.png"),
                ("growth",       "09-growth.png"),
                ("quick-launch", "10-quick-launch.png"),
                ("tools-hub",    "11-streamer-tools.png"),
                ("settings",     "12-settings.png"),
            };

            BitmapSource? dashboard = null, liveControl = null;
            var frame = GameFrame(1280, 720);

            foreach (var (tag, file) in shots)
            {
                MainWindow.NavigateTo?.Invoke(tag);
                await Settle(900);
                var view = ((ContentControl)window.FindName("MainContent")).Content;
                if (view is DashboardView d) d.ApplyStoreDemo();
                if (view is LiveControlView l) l.ApplyStoreDemo(frame);
                await Settle(700);

                var bmp = Render(root, W, H);
                Save(bmp, file);
                if (tag == "dashboard")    dashboard   = bmp;
                if (tag == "live-control") liveControl = bmp;
                if (_tiktok) break;
            }

            if (_tiktok)
            {
                TikTokVideo.Render(dashboard!, _outDir);
                return;
            }

            // Pro modal, composited over the dimmed dashboard the way it appears in-app.
            MainWindow.NavigateTo?.Invoke("dashboard");
            await Settle(600);
            var pro = new ProUpgradeWindow { Left = -5000, Top = -5000, ShowActivated = false };
            pro.Show();
            await Settle(1800);   // live Store prices load async
            var proRoot = (FrameworkElement)pro.Content;
            var proBmp  = Render(proRoot, (int)proRoot.ActualWidth, (int)proRoot.ActualHeight);
            pro.Close();
            Save(Compose(dashboard!, proBmp), "03-pro-upgrade.png");

            Save(HeroMockup(dashboard!), "mockup-1-hero.png");
            Save(InGameMockup(frame, liveControl!), "mockup-2-in-game.png");
        }
        catch (Exception ex)
        {
            File.WriteAllText(System.IO.Path.Combine(_outDir, "capture-error.txt"), ex.ToString());
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    // ── Rendering helpers ────────────────────────────────────────────────────

    private static async Task Settle(int ms)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static BitmapSource Render(FrameworkElement el, int w, int h)
    {
        el.Measure(new Size(w, h));
        el.Arrange(new Rect(0, 0, w, h));
        el.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(el);
        rtb.Freeze();
        return rtb;
    }

    private static void Save(BitmapSource bmp, string file)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(System.IO.Path.Combine(_outDir, file));
        enc.Save(fs);
    }

    private static BitmapSource Compose(BitmapSource background, BitmapSource modal)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(background, new Rect(0, 0, W, H));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 4, 7, 11)), null, new Rect(0, 0, W, H));
            dc.DrawImage(modal, new Rect((W - modal.PixelWidth) / 2.0, (H - modal.PixelHeight) / 2.0, modal.PixelWidth, modal.PixelHeight));
        }
        return RenderVisual(dv, W, H);
    }

    private static BitmapSource RenderVisual(Visual v, int w, int h)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        rtb.Freeze();
        return rtb;
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Brush B(string hex) => new SolidColorBrush(Hex(hex));
    private static readonly FontFamily Font = new("Bahnschrift, Segoe UI");
    private static readonly FontFamily Condensed = new("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI");

    /// <summary>
    /// A generic, original FPS-style frame (sunset horizon, grid floor, HUD) for the preview and
    /// the in-game mockup — drawn from scratch so the listing carries no third-party game art.
    /// </summary>
    public static BitmapSource GameFrame(int w, int h, bool hud = true)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double horizon = h * 0.56;
            dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection
            {
                new(Hex("#081426"), 0), new(Hex("#173d63"), 0.55), new(Hex("#d9844a"), 0.93), new(Hex("#f2b36b"), 1),
            }, 90), null, new Rect(0, 0, w, horizon));

            // Sun + haze
            dc.DrawEllipse(new RadialGradientBrush(Hex("#ffe2a8"), Color.FromArgb(0, 0xff, 0xb3, 0x6b)), null,
                           new Point(w * 0.64, horizon - h * 0.02), w * 0.16, w * 0.16);

            // Two mountain ranges
            Ridge(dc, w, horizon, h * 0.22, Hex("#1c2f4a"), 7, 0.3);
            Ridge(dc, w, horizon, h * 0.13, Hex("#101c2e"), 11, 1.7);

            // Ground with a perspective grid
            dc.DrawRectangle(new LinearGradientBrush(Hex("#0d1a29"), Hex("#060b12"), 90), null, new Rect(0, horizon, w, h - horizon));
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0xC9, 0xA7)), 1.2);
            var vp = new Point(w * 0.5, horizon);
            for (int i = -14; i <= 14; i++) dc.DrawLine(grid, vp, new Point(w * 0.5 + i * w * 0.09, h));
            for (int i = 1; i <= 9; i++)
            {
                double t = Math.Pow(i / 9.0, 2.2);
                double y = horizon + (h - horizon) * t;
                dc.DrawLine(grid, new Point(0, y), new Point(w, y));
            }

            // Crosshair always; health, ammo and minimap only when asked (they collide with mockup copy)
            var white = new Pen(new SolidColorBrush(Color.FromArgb(0xE6, 0xff, 0xff, 0xff)), 2);
            var c = new Point(w / 2.0, h / 2.0);
            dc.DrawLine(white, new Point(c.X - 18, c.Y), new Point(c.X - 6, c.Y));
            dc.DrawLine(white, new Point(c.X + 6, c.Y), new Point(c.X + 18, c.Y));
            dc.DrawLine(white, new Point(c.X, c.Y - 18), new Point(c.X, c.Y - 6));
            dc.DrawLine(white, new Point(c.X, c.Y + 6), new Point(c.X, c.Y + 18));

            if (hud)
            {
                double m = h * 0.045;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)), null, new Rect(m, h - m - 34, w * 0.22, 34), 6, 6);
                dc.DrawRoundedRectangle(new SolidColorBrush(Hex("#3be08c")), null, new Rect(m + 6, h - m - 28, w * 0.22 * 0.78 - 12, 22), 4, 4);
                Text(dc, "100 / 30", w - m - 170, h - m - 52, 40, Brushes.White, bold: true);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)), new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0xff, 0xff, 0xff)), 2),
                               new Point(m + h * 0.11, m + h * 0.11), h * 0.11, h * 0.11);
                dc.DrawEllipse(new SolidColorBrush(Hex("#00C9A7")), null, new Point(m + h * 0.11, m + h * 0.11), 5, 5);
            }
        }
        return RenderVisual(dv, w, h);
    }

    private static void Ridge(DrawingContext dc, int w, double baseY, double height, Color fill, int peaks, double phase)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0, baseY), true, true);
            for (int i = 0; i <= peaks * 2; i++)
            {
                double x = w * i / (peaks * 2.0);
                double y = baseY - height * (0.45 + 0.55 * Math.Abs(Math.Sin(i * 1.3 + phase))) * (i % 2 == 0 ? 0.55 : 1);
                ctx.LineTo(new Point(x, y), true, false);
            }
            ctx.LineTo(new Point(w, baseY), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(new SolidColorBrush(fill), null, g);
    }

    private static void Text(DrawingContext dc, string s, double x, double y, double size, Brush b, bool bold = false)
        => dc.DrawText(new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, b, 1.0), new Point(x, y));

    // ── Mockups ──────────────────────────────────────────────────────────────

    /// <summary>Headline + the live dashboard: the one-glance pitch for the listing's first slot.</summary>
    private static BitmapSource HeroMockup(BitmapSource dashboard)
    {
        var root = new Grid { Width = W, Height = H, Background = B("#070b11"), ClipToBounds = true };
        root.Children.Add(new Ellipse { Width = 1400, Height = 1000, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -380, -360, 0),
            Fill = new RadialGradientBrush(Color.FromArgb(0x40, 0x00, 0xC9, 0xA7), Color.FromArgb(0, 0x00, 0xC9, 0xA7)) });
        root.Children.Add(new Ellipse { Width = 900, Height = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-300, 0, 0, -320),
            Fill = new RadialGradientBrush(Color.FromArgb(0x26, 0x00, 0xC9, 0xA7), Color.FromArgb(0, 0x00, 0xC9, 0xA7)) });

        // Screenshot, framed and glowing, bleeding off the right edge
        var shot = new Border
        {
            Width = 1320, Height = 742, CornerRadius = new CornerRadius(14), ClipToBounds = true,
            BorderBrush = B("#2a4a5a"), BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(760, 60, 0, 0),
            Background = new ImageBrush(dashboard) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
            Effect = new DropShadowEffect { Color = Hex("#00C9A7"), BlurRadius = 60, ShadowDepth = 0, Opacity = 0.45 },
        };
        root.Children.Add(shot);

        var copy = new StackPanel { Margin = new Thickness(110, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 620, HorizontalAlignment = HorizontalAlignment.Left };
        copy.Children.Add(new TextBlock { Text = "STREAM COMMAND", FontFamily = Condensed, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = B("#4fe3c8") });
        copy.Children.Add(new TextBlock { Text = "Control your whole stream without leaving your game.", FontFamily = Font, FontSize = 64,
            FontWeight = FontWeights.Bold, Foreground = B("#e8edf2"), TextWrapping = TextWrapping.Wrap, LineHeight = 70, Margin = new Thickness(0, 14, 0, 0) });
        copy.Children.Add(new TextBlock { Text = "Switch scenes, manage audio and go live — no alt-tabbing out of the match.", FontFamily = Font, FontSize = 26,
            Foreground = B("#a9bacb"), TextWrapping = TextWrapping.Wrap, LineHeight = 36, Margin = new Thickness(0, 22, 0, 34) });
        copy.Children.Add(KeyRow("Ctrl + Shift + F1–F8", "Scenes"));
        copy.Children.Add(KeyRow("Ctrl + Shift + F10", "Mic"));
        copy.Children.Add(KeyRow("Ctrl + Shift + F11", "Go live"));
        root.Children.Add(copy);
        return Render(root, W, H);
    }

    private static UIElement KeyRow(string keys, string what)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        row.Children.Add(new Border
        {
            Background = B("#0b1420"), BorderBrush = B("#2a3a4a"), BorderThickness = new Thickness(1.5, 1.5, 1.5, 3),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 6, 14, 6),
            Child = new TextBlock { Text = keys, FontFamily = Condensed, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = B("#e8edf2") },
        });
        row.Children.Add(new TextBlock { Text = what, FontFamily = Font, FontSize = 24, Foreground = B("#8a9db3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        return row;
    }

    /// <summary>The moment that sells it: mid-match, a shortcut flips the scene and the game keeps focus.</summary>
    private static BitmapSource InGameMockup(BitmapSource frame, BitmapSource liveControl)
    {
        var root = new Grid { Width = W, Height = H, ClipToBounds = true,
            Background = new ImageBrush(GameFrame(W, H, hud: false)) { Stretch = Stretch.Fill } };
        root.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xD0, 4, 7, 11), 90) });

        // The real hotkey toast, as it appears over a game
        var toast = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 54, 0, 0),
            Background = B("#0f1824"), BorderBrush = B("#00C9A7"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(26, 18, 34, 18),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 6, Opacity = 0.6, Direction = 270 },
        };
        var tRow = new StackPanel { Orientation = Orientation.Horizontal };
        tRow.Children.Add(new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(14), Background = B("#0f2a2a"), Margin = new Thickness(0, 0, 20, 0),
            Child = new Ellipse { Width = 16, Height = 16, Fill = B("#00C9A7") } });
        var tText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tText.Children.Add(new TextBlock { Text = "Gameplay", FontFamily = Font, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = B("#e8edf2") });
        tText.Children.Add(new TextBlock { Text = "Now on program", FontFamily = Font, FontSize = 20, Foreground = B("#8a9db3") });
        tRow.Children.Add(tText);
        toast.Child = tRow;
        root.Children.Add(toast);

        // Caption bottom-left
        var cap = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(110, 0, 0, 96), Width = 900 };
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22) };
        foreach (var k in new[] { "Ctrl", "Shift", "F2" })
            keys.Children.Add(new Border
            {
                Background = B("#0b1420"), BorderBrush = B("#00C9A7"), BorderThickness = new Thickness(2, 2, 2, 5), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(22, 10, 22, 10), Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = k, FontFamily = Condensed, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = B("#e8edf2") },
            });
        cap.Children.Add(keys);
        cap.Children.Add(new TextBlock { Text = "Switch scenes mid-match.", FontFamily = Font, FontSize = 62, FontWeight = FontWeights.Bold, Foreground = B("#ffffff") });
        cap.Children.Add(new TextBlock { Text = "Your game never loses focus — Stream Command works in the background.", FontFamily = Font, FontSize = 26,
            Foreground = B("#c4d0dc"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(cap);

        // Live Control on the "second monitor", bottom-right
        root.Children.Add(new Border
        {
            Width = 640, Height = 360, CornerRadius = new CornerRadius(12), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 96, 96),
            BorderBrush = B("#2a4a5a"), BorderThickness = new Thickness(1.5),
            Background = new ImageBrush(liveControl) { Stretch = Stretch.UniformToFill },
            Effect = new DropShadowEffect { Color = Hex("#00C9A7"), BlurRadius = 40, ShadowDepth = 0, Opacity = 0.4 },
        });
        return Render(root, W, H);
    }
}
#endif
