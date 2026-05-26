using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace StreamCommand.Services;

/// <summary>
/// Finds the OBS Virtual Camera via WinRT MediaCapture, captures frames and delivers
/// frozen <see cref="BitmapSource"/> objects to registered subscribers on the UI thread.
///
/// Multiple views can subscribe simultaneously — the physical device is opened only
/// once and closed when the last subscriber unregisters.
/// </summary>
public sealed class VirtualCameraService
{
    public static readonly VirtualCameraService Instance = new();
    private VirtualCameraService() { }

    // ── State ─────────────────────────────────────────────────────────────────

    private MediaCapture?     _mediaCapture;
    private MediaFrameReader? _frameReader;
    private event Action<BitmapSource>? _frameReady;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsCapturing { get; private set; }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Returns true when OBS Virtual Camera is available as a DirectShow device.</summary>
    public async Task<bool> FindOBSCameraAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            foreach (var d in devices)
                if (d.Name.Contains("OBS Virtual Camera", StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Registers <paramref name="onFrame"/> as a frame subscriber and starts the
    /// capture device if it is not already running.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when capture started (or was already running).
    /// <see langword="false"/> when OBS Virtual Camera was not found.
    /// </returns>
    public async Task<bool> StartCaptureAsync(Action<BitmapSource> onFrame)
    {
        await _gate.WaitAsync();
        try
        {
            _frameReady += onFrame;
            if (IsCapturing) return true;   // already running — subscriber added, device stays open

            // Find OBS Virtual Camera
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            DeviceInformation? obsDevice = null;
            foreach (var d in devices)
                if (d.Name.Contains("OBS Virtual Camera", StringComparison.OrdinalIgnoreCase))
                { obsDevice = d; break; }

            if (obsDevice is null) { _frameReady -= onFrame; return false; }

            // Initialise capture — Cpu memory so we can read pixels without GPU roundtrip
            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId        = obsDevice.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference     = MediaCaptureMemoryPreference.Cpu,
                SharingMode          = MediaCaptureSharingMode.SharedReadOnly
            });

            // Pick first video frame source (preview or record stream)
            MediaFrameSource? source = null;
            foreach (var s in _mediaCapture.FrameSources.Values)
                if (s.Info.MediaStreamType is MediaStreamType.VideoPreview
                                           or MediaStreamType.VideoRecord)
                { source = s; break; }

            if (source is null)
            {
                _frameReady -= onFrame;
                _mediaCapture.Dispose(); _mediaCapture = null;
                return false;
            }

            // Request BGRA8 — Windows converts NV12/YUV automatically in the media pipeline
            _frameReader = await _mediaCapture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _frameReader.FrameArrived += OnFrameArrived;
            await _frameReader.StartAsync();

            IsCapturing = true;
            return true;
        }
        catch
        {
            _frameReady -= onFrame;
            try { _mediaCapture?.Dispose(); } catch { }
            _mediaCapture = null;
            return false;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Unregisters <paramref name="handler"/> from frame delivery.
    /// When null, all subscribers are removed.
    /// Device stops automatically when no subscribers remain.
    /// </summary>
    public async Task StopCaptureAsync(Action<BitmapSource>? handler = null)
    {
        MediaFrameReader? readerToStop  = null;
        MediaCapture?     captureToStop = null;

        await _gate.WaitAsync();
        try
        {
            if (handler is not null) _frameReady -= handler;
            else                     _frameReady  = null;

            int remaining = _frameReady?.GetInvocationList()?.Length ?? 0;
            if (remaining == 0 && _frameReader is not null)
            {
                readerToStop  = _frameReader;
                captureToStop = _mediaCapture;
                _frameReader  = null;
                _mediaCapture = null;
                IsCapturing   = false;
            }
        }
        finally { _gate.Release(); }

        if (readerToStop is not null)
        {
            readerToStop.FrameArrived -= OnFrameArrived;
            try { await readerToStop.StopAsync(); } catch { }
            readerToStop.Dispose();
            captureToStop?.Dispose();
        }
    }

    // ── Frame pipeline ─────────────────────────────────────────────────────────

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var softBitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (softBitmap is null) return;

            // Normalise to Bgra8 with non-premultiplied alpha (standard for game capture)
            SoftwareBitmap bgra;
            if (softBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8)
                bgra = SoftwareBitmap.Copy(softBitmap);
            else
                bgra = SoftwareBitmap.Convert(softBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);

            int    w         = bgra.PixelWidth;
            int    h         = bgra.PixelHeight;
            uint   byteCount = (uint)(4 * w * h);

            // Copy pixels into managed byte array via WinRT Buffer + DataReader
            var winrtBuf = new Windows.Storage.Streams.Buffer(byteCount) { Length = byteCount };
            bgra.CopyToBuffer(winrtBuf);
            bgra.Dispose();

            var pixels = new byte[byteCount];
            using var reader = DataReader.FromBuffer(winrtBuf);
            reader.ReadBytes(pixels);

            // Build a frozen BitmapSource — freeze makes it cross-thread safe
            var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, 4 * w);
            bs.Freeze();

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Action<BitmapSource>? subs;
                lock (this) { subs = _frameReady; }
                subs?.Invoke(bs);
            }));
        }
        catch { }
    }
}
