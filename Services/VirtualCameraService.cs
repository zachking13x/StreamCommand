using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;

namespace StreamCommand.Services;

/// <summary>
/// Finds the OBS Virtual Camera DirectShow device, captures frames, and delivers
/// frozen <see cref="BitmapSource"/> objects to registered subscribers on the UI thread.
///
/// Multiple views can subscribe simultaneously — the physical device is opened only
/// once and closed when the last subscriber unregisters.
///
/// NOTE: AForge delivers frames as BGR24 (3 bytes/pixel). We copy the raw pixel data
/// with LockBits and create a Bgr24 BitmapSource — do NOT clone to ARGB32 first, as
/// that produces a misaligned 4-byte buffer interpreted as 3-byte rows (black frames).
/// </summary>
public sealed class VirtualCameraService
{
    public static readonly VirtualCameraService Instance = new();
    private VirtualCameraService() { }

    // ── State ─────────────────────────────────────────────────────────────────

    private VideoCaptureDevice? _device;
    private event Action<BitmapSource>? _frameReady;
    private readonly object _lock = new();

    public bool IsCapturing { get; private set; }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Returns true when OBS Virtual Camera is present as a DirectShow device.</summary>
    public Task<bool> FindOBSCameraAsync()
    {
        try
        {
            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo d in devices)
                if (d.Name.Contains("OBS Virtual Camera", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(true);
        }
        catch { }
        return Task.FromResult(false);
    }

    /// <summary>
    /// Registers <paramref name="onFrame"/> as a frame subscriber and starts the
    /// capture device if it is not already running.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when capture started successfully (or was already running).
    /// <see langword="false"/> when OBS Virtual Camera was not found.
    /// </returns>
    public Task<bool> StartCaptureAsync(Action<BitmapSource> onFrame)
    {
        lock (_lock)
        {
            _frameReady += onFrame;

            if (IsCapturing) return Task.FromResult(true);  // already running

            // Find OBS Virtual Camera
            FilterInfo? camera = null;
            try
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                foreach (FilterInfo d in devices)
                    if (d.Name.Contains("OBS Virtual Camera", StringComparison.OrdinalIgnoreCase))
                    { camera = d; break; }
            }
            catch { }

            if (camera == null)
            {
                _frameReady -= onFrame;
                return Task.FromResult(false);
            }

            _device = new VideoCaptureDevice(camera.MonikerString);

            // Set explicit capture format — without this AForge may negotiate
            // a format OBS Virtual Camera doesn't deliver frames on
            var videoCapabilities = _device.VideoCapabilities;
            if (videoCapabilities != null && videoCapabilities.Length > 0)
            {
                // Pick the highest resolution available — OBS Virtual Camera
                // lists its stream resolution as the first/only capability
                _device.VideoResolution = videoCapabilities[0];
            }

            _device.NewFrame += OnNewFrame;
            _device.Start();
            IsCapturing = true;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Unregisters <paramref name="handler"/> from frame delivery (null = remove all).
    /// Device stops automatically when no subscribers remain.
    /// </summary>
    public async Task StopCaptureAsync(Action<BitmapSource>? handler = null)
    {
        VideoCaptureDevice? deviceToStop = null;

        lock (_lock)
        {
            if (handler != null)
                _frameReady -= handler;
            else
                _frameReady = null;

            int remaining = _frameReady?.GetInvocationList()?.Length ?? 0;
            if (remaining == 0 && _device != null)
            {
                deviceToStop = _device;
                _device      = null;
                IsCapturing  = false;
            }
        }

        // Stop off the UI thread so WaitForStop() doesn't freeze the app
        if (deviceToStop != null)
            await Task.Run(() =>
            {
                try
                {
                    deviceToStop.NewFrame -= OnNewFrame;
                    deviceToStop.SignalToStop();
                    deviceToStop.WaitForStop();
                }
                catch { }
            });
    }

    // ── Frame pipeline ─────────────────────────────────────────────────────────

    private void OnNewFrame(object sender, NewFrameEventArgs e)
    {
        try
        {
            var bmp = e.Frame;   // AForge owns this — do NOT dispose; copy pixels out fast
            int w = bmp.Width, h = bmp.Height;

            // Lock as BGR24 (Format24bppRgb is stored B-G-R in memory on Windows,
            // matching WPF's Bgr24 pixel format perfectly — no channel swap needed).
            var data = bmp.LockBits(
                new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            byte[] pixels;
            int stride;
            try
            {
                stride = data.Stride;                       // may be padded to 4-byte alignment
                pixels = new byte[Math.Abs(stride) * h];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            // Build frozen BitmapSource — Bgr24 matches the raw BGR24 bytes exactly
            var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
            bs.Freeze();    // make cross-thread safe

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Action<BitmapSource>? subs;
                lock (_lock) { subs = _frameReady; }
                subs?.Invoke(bs);
            }));
        }
        catch { }
    }
}
