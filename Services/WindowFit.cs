using System.Windows;

namespace StreamCommand.Services;

/// <summary>
/// Keeps a window inside the usable screen. Store cert (10.1.2.10) tests at 200% on a 2560x1600
/// laptop: that is a ~1280x750 work area in WPF units, smaller than the designed sizes, so the
/// bottom of the main window sat under the taskbar. Windows shrink to fit and their content scrolls.
/// </summary>
public static class WindowFitMath
{
    public readonly record struct Size(double Width, double Height, double MinWidth, double MinHeight);

    public static Size ToWorkArea(double width, double height, double minWidth, double minHeight,
                                  double workWidth, double workHeight)
    {
        // Min sizes must give way too, or WPF re-grows the window past the screen edge.
        var minW = Math.Min(minWidth, workWidth);
        var minH = Math.Min(minHeight, workHeight);
        // NaN = SizeToContent; those dialogs are small enough to leave alone.
        var w = double.IsNaN(width) ? width : Math.Max(minW, Math.Min(width, workWidth));
        var h = double.IsNaN(height) ? height : Math.Max(minH, Math.Min(height, workHeight));
        return new(w, h, minW, minH);
    }
}

public static class WindowFit
{
    /// <summary>Call after InitializeComponent, before the window is shown, so Center* placement uses the fitted size.</summary>
    public static void Apply(Window window)
    {
        // Primary-monitor work area in device-independent units; Center* placement opens there.
        var work = SystemParameters.WorkArea;
        var fit = WindowFitMath.ToWorkArea(window.Width, window.Height, window.MinWidth, window.MinHeight,
                                           work.Width, work.Height);
        window.MinWidth = fit.MinWidth;
        window.MinHeight = fit.MinHeight;
        window.Width = fit.Width;
        window.Height = fit.Height;
        // No MaxWidth/MaxHeight: it would cap maximising on a larger second monitor.
    }
}
