using System.Runtime.InteropServices;
using System.Windows;

namespace StreamCommand.Services;

/// <summary>Who owns the screen right now, and handing focus back after another app grabs it.</summary>
public static class ForegroundWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    public static IntPtr Current => GetForegroundWindow();

    /// <summary>
    /// True when the foreground window belongs to another app and covers its whole monitor —
    /// a full-screen game or video we must not interrupt. Must run on the UI thread (it checks our own windows).
    /// </summary>
    public static bool IsOtherAppFullScreen()
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            // Our own windows never count as "don't disturb".
            foreach (Window w in Application.Current.Windows)
                if (new System.Windows.Interop.WindowInteropHelper(w).Handle == fg) return false;

            if (!GetWindowRect(fg, out var wr)) return false;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(fg, 2 /* MONITOR_DEFAULTTONEAREST */), ref info)) return false;

            return wr.Left  <= info.Monitor.Left  && wr.Top    <= info.Monitor.Top &&
                   wr.Right >= info.Monitor.Right && wr.Bottom >= info.Monitor.Bottom;
        }
        catch { return false; }
    }

    /// <summary>Process that owns the current foreground window (0 if none).</summary>
    public static uint CurrentProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid;
    }

    /// <summary>Best effort: Windows may refuse (foreground lock), in which case nothing happens.</summary>
    public static bool TryRestore(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd) && SetForegroundWindow(hwnd);
}
