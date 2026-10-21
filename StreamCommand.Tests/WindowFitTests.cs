using StreamCommand.Services;
using Xunit;

namespace StreamCommand.Tests;

public class WindowFitTests
{
    // The cert failure: 2560x1600 at 200% leaves ~1280x752 after the taskbar; MainWindow was 1280x780.
    [Fact]
    public void MainWindow_ShrinksToCertDeviceWorkArea()
    {
        var fit = WindowFitMath.ToWorkArea(1280, 780, 960, 620, 1280, 752);
        Assert.Equal(1280, fit.Width);
        Assert.Equal(752, fit.Height);
        Assert.Equal(620, fit.MinHeight);
    }

    [Fact]
    public void FitsAlready_Unchanged()
    {
        var fit = WindowFitMath.ToWorkArea(1280, 780, 960, 620, 2560, 1392);
        Assert.Equal(new WindowFitMath.Size(1280, 780, 960, 620), fit);
    }

    // 1920x1080 at 250% → 768x408: even the minimum size must yield, or WPF regrows past the screen.
    [Fact]
    public void WorkAreaBelowMinimum_MinimumYields()
    {
        var fit = WindowFitMath.ToWorkArea(1280, 780, 960, 620, 768, 408);
        Assert.Equal(new WindowFitMath.Size(768, 408, 768, 408), fit);
    }

    [Fact]
    public void SizeToContent_NaNLeftAlone()
    {
        var fit = WindowFitMath.ToWorkArea(460, double.NaN, 0, 0, 1280, 752);
        Assert.Equal(460, fit.Width);
        Assert.True(double.IsNaN(fit.Height));
    }
}
