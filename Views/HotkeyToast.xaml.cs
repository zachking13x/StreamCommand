using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StreamCommand.Services;

namespace StreamCommand.Views;

/// <summary>
/// Confirmation toast for global shortcuts, shown top-centre over the game. It must never take
/// focus or eat clicks — stealing focus from a game mid-fight is the thing this app exists to avoid.
/// </summary>
public partial class HotkeyToast : Window
{
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(2.4) };

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    public HotkeyToast()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        _hide.Tick += (_, _) => { _hide.Stop(); Fade(0); };
        HotkeyCommands.Feedback += (headline, detail, warn) => Dispatcher.BeginInvoke(() => Show(headline, detail, warn));
    }

    public void Show(string headline, string detail, bool warn)
    {
        Headline.Text = headline;
        Detail.Text   = detail;
        Card.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, warn ? "WarningBrush" : "AccentBrush");
        ToastIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, warn ? "WarningBrush" : "AccentBrush");

        if (!IsVisible) base.Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top  = area.Top + 24;

        Fade(1);
        _hide.Stop();
        _hide.Start();
    }

    private void Fade(double to) => BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)));
}
