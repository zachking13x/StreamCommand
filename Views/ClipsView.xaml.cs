using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

public partial class ClipsView : UserControl
{
    private static ClipService Clips => ClipService.Shared;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _loading;
    private ClipSessionState _lastState = ClipSessionState.Idle;

    public ClipsView()
    {
        InitializeComponent();
        LoadOptions();

        Clips.Changed += OnClipsChanged;
        _tick.Tick += (_, _) => RefreshSession();
        Loaded   += (_, _) => { _tick.Start(); RefreshSession(); RefreshFfmpeg(); RefreshClipList(); };
        Unloaded += (_, _) => _tick.Stop();
    }

    private void OnClipsChanged() => Dispatcher.BeginInvoke(() =>
    {
        RefreshSession();
        // A session just finished rendering — new files are on disk.
        if (_lastState == ClipSessionState.Rendering && Clips.State == ClipSessionState.Idle) RefreshClipList();
        _lastState = Clips.State;
    });

    // ── Session ──────────────────────────────────────────────────────────────

    private void RefreshSession()
    {
        var state = Clips.State;
        StatusText.Text = Clips.Status;
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, state switch
        {
            ClipSessionState.Recording => "LiveBrush",
            ClipSessionState.Rendering or ClipSessionState.Starting => "WarningBrush",
            _ => "MutedText",
        });
        ElapsedText.Text = Clips.SessionStarted is DateTime t ? (DateTime.Now - t).ToString(@"h\:mm\:ss") : "";

        StartStopBtn.Content   = state == ClipSessionState.Recording ? "Stop clipping" : "Start clipping";
        StartStopBtn.IsEnabled = state is ClipSessionState.Idle or ClipSessionState.Recording;
        ClipThatBtn.IsEnabled  = state == ClipSessionState.Recording;

        var moments = Clips.Moments;
        MomentsText.Text = moments.Count == 0
            ? (state == ClipSessionState.Recording ? "No highlights marked yet." : "")
            : $"{moments.Count} highlight{(moments.Count == 1 ? "" : "s")} marked: " +
              string.Join(", ", moments.TakeLast(6).Select(m => $"{m.At:h\\:mm\\:ss} {m.Reason}"));

        FacecamText.Text = state == ClipSessionState.Recording
            ? (Clips.FacecamDetected ? "Webcam found in your OBS scene. It goes on top of each clip."
                                     : "No webcam in your current OBS scene, so clips use the blurred-background layout.")
            : "The facecam is found automatically from your OBS scene when clipping starts.";
    }

    private async void StartStop_Click(object sender, RoutedEventArgs e)
    {
        StartStopBtn.IsEnabled = false;
        if (Clips.State == ClipSessionState.Recording)
            await Clips.StopSessionAsync();
        else if (await Clips.StartSessionAsync() is { } error)
            DetailText.Text = error;
        RefreshSession();
    }

    private async void ClipThat_Click(object sender, RoutedEventArgs e)
    {
        var (headline, detail, _) = await Clips.ClipThatAsync();
        DetailText.Text = $"{headline} {detail}";
        RefreshSession();
    }

    // ── Options ──────────────────────────────────────────────────────────────

    private void LoadOptions()
    {
        _loading = true;
        var s = SettingsService.Load();
        ClipWithStreamToggle.IsChecked = s.ClipWithStream;
        AutoClipToggle.IsChecked       = s.AutoClipEnabled;
        SaveFullToggle.IsChecked       = s.SaveFullStream;
        Select(LayoutCombo,   s.ClipLayout);
        Select(LengthCombo,   s.ClipPreSeconds.ToString());
        Select(MaxClipsCombo, s.MaxClipsPerStream.ToString());
        FolderText.Text = ClipService.ClipsRoot(s);
        _loading = false;
    }

    private static void Select(ComboBox combo, string tag)
        => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag)
                                ?? combo.Items[Math.Min(1, combo.Items.Count - 1)];

    private static string? TagOf(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = SettingsService.Load();
        s.ClipWithStream  = ClipWithStreamToggle.IsChecked == true;
        s.AutoClipEnabled = AutoClipToggle.IsChecked == true;
        s.SaveFullStream  = SaveFullToggle.IsChecked == true;
        s.ClipLayout      = TagOf(LayoutCombo) ?? s.ClipLayout;
        if (int.TryParse(TagOf(LengthCombo), out var pre))    s.ClipPreSeconds    = pre;
        if (int.TryParse(TagOf(MaxClipsCombo), out var max))  s.MaxClipsPerStream = max;
        SettingsService.Save(s);
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Load();
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Where should clips be saved?", InitialDirectory = ClipService.ClipsRoot(s) };
        if (dlg.ShowDialog() != true) return;
        s.ClipsFolder = dlg.FolderName;
        SettingsService.Save(s);
        FolderText.Text = dlg.FolderName;
        RefreshClipList();
    }

    private void LocateFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Find ffmpeg.exe", Filter = "ffmpeg|ffmpeg.exe" };
        if (dlg.ShowDialog() != true) return;
        var s = SettingsService.Load();
        s.FfmpegPath = dlg.FileName;
        SettingsService.Save(s);
        RefreshFfmpeg();
    }

    private void RefreshFfmpeg()
    {
        var path = ClipRenderer.FindFfmpeg(SettingsService.Load().FfmpegPath);
        FfmpegText.Text = path is not null
            ? $"Ready: {path}"
            : "Not found. Clips need the free ffmpeg tool. Install it by running  winget install Gyan.FFmpeg  in a terminal, then reopen this page, or click Locate to point at ffmpeg.exe.";
        FfmpegText.SetResourceReference(TextBlock.ForegroundProperty, path is not null ? "PrimaryText" : "WarningBrush");
    }

    // ── Recent clips ─────────────────────────────────────────────────────────

    private void RefreshClipList()
    {
        ClipList.Children.Clear();
        var root = ClipService.ClipsRoot(SettingsService.Load());
        var files = Directory.Exists(root)
            ? new DirectoryInfo(root).EnumerateFiles("*.mp4", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTime).Take(30).ToList()
            : [];
        NoClipsText.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var file in files)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = file.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            ((TextBlock)text.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            var meta = new TextBlock { Text = $"{file.LastWriteTime:MMM d, h:mm tt} · {file.Length / 1048576.0:0.0} MB", FontSize = 11 };
            meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
            text.Children.Add(meta);
            row.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(MakeButton("Play", () => Shell(file.FullName)));
            buttons.Children.Add(MakeButton("Show in folder", () => Shell("explorer.exe", $"/select,\"{file.FullName}\"")));
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            ClipList.Children.Add(row);
        }
    }

    private Button MakeButton(string label, Action onClick)
    {
        var b = new Button { Content = label, Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Clips.LastClipsFolder ?? ClipService.ClipsRoot(SettingsService.Load());
        Directory.CreateDirectory(folder);
        Shell(folder);
    }

    private static void Shell(string target, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(target) { Arguments = args ?? "", UseShellExecute = true }); }
        catch { /* no handler for the file type — nothing useful to add */ }
    }
}
