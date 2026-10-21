using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StreamCommand.Services;

/// <summary>
/// Registers the shortcut table system-wide via RegisterHotKey, so it fires while a game has
/// focus. Registration is per-window: attach once to the main window.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public static GlobalHotkeyService Instance { get; } = new();

    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private readonly List<int> _registered = [];
    private IReadOnlyList<HotkeyBinding> _bindings = HotkeyBindings.For(HotkeyModifierSet.CtrlShift);

    public event Action<HotkeyBinding>? Pressed;

    /// <summary>Shortcuts another app already owns (RegisterHotKey refused them).</summary>
    public IReadOnlyList<HotkeyBinding> Conflicts { get; private set; } = [];

    public bool IsActive => _registered.Count > 0;

    public void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(WndProc);
    }

    /// <summary>(Re)registers every binding with the given modifier; returns the ones that failed.</summary>
    public IReadOnlyList<HotkeyBinding> Enable(HotkeyModifierSet set)
    {
        Disable();
        _bindings = HotkeyBindings.For(set);
        // Every on-screen shortcut label binds to this, so chips follow the chosen modifier.
        Application.Current.Resources["HotkeyPrefix"] = HotkeyBindings.PrefixFor(set);
        if (_source == null) return [];

        var failed = new List<HotkeyBinding>();
        foreach (var b in _bindings)
        {
            if (RegisterHotKey(_source.Handle, b.Id, b.Modifiers, b.VirtualKey)) _registered.Add(b.Id);
            else failed.Add(b);   // ERROR_HOTKEY_ALREADY_REGISTERED — another app owns it
        }
        Conflicts = failed;
        return failed;
    }

    public void Disable()
    {
        if (_source != null)
            foreach (var id in _registered) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        Conflicts = [];
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _bindings.FirstOrDefault(b => b.Id == wParam.ToInt32()) is { } binding)
        {
            handled = true;
            Pressed?.Invoke(binding);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Disable();
        _source?.RemoveHook(WndProc);
    }
}
