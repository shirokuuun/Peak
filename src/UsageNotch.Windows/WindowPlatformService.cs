using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace UsageNotch.Windows;

public sealed class WindowPlatformService : IDisposable
{
    private const int HotkeyId = 0x554E;
    private Window? _window;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool? _clickThrough;
    private System.Windows.Rect? _interactionBounds;
    public bool HotkeyAvailable { get; private set; }
    public void SetRecoveryShortcut(string shortcut)
    {
        if (_handle == IntPtr.Zero) return;
        if (HotkeyAvailable) UnregisterHotKey(_handle, HotkeyId);
        var (modifiers, key) = shortcut switch
        {
            "CtrlAltP" => (0x4003, 0x50), "CtrlShiftSpace" => (0x4006, 0x20), _ => (0x4003, 0x55)
        };
        HotkeyAvailable = RegisterHotKey(_handle, HotkeyId, modifiers, key);
    }
    public bool IsPointerOver()
    {
        if (_handle == IntPtr.Zero || !GetCursorPos(out var point) || !GetWindowRect(_handle, out var rect)) return false;
        if (_interactionBounds is { } bounds)
        {
            var scale = GetDpiForWindow(_handle) / 96d;
            if (scale <= 0) scale = 1;
            return bounds.Contains(new System.Windows.Point((point.X - rect.Left) / scale, (point.Y - rect.Top) / scale));
        }
        return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }
    public void SetInteractionBounds(System.Windows.Rect bounds) => _interactionBounds = bounds;
    public static bool AltHeld => (GetAsyncKeyState(0x12) & 0x8000) != 0;
    public bool SetCaptureExclusion(bool enabled) => _handle != IntPtr.Zero && SetWindowDisplayAffinity(_handle, enabled ? 0x11u : 0u);
    public event EventHandler? HotkeyPressed;
    public event EventHandler? DisplayChanged;

    public void Attach(Window window)
    {
        _window = window;
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        HotkeyAvailable = RegisterHotKey(_handle, HotkeyId, 0x4003, 0x55); // Ctrl+Alt+U, no repeat
    }

    public static IReadOnlyList<string> MonitorNames => ["Primary", .. Forms.Screen.AllScreens.Select(s => s.DeviceName)];
    private static Forms.Screen? Screen(string device) => device == "Primary" ? Forms.Screen.PrimaryScreen :
        Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? Forms.Screen.PrimaryScreen;

    public void PositionAtTopCenter(int offset, string monitor = "Primary", bool attachedToTop = false)
    {
        if (_window is null || _handle == IntPtr.Zero || Screen(monitor) is not { } screen) return;
        // Keep coordinates in physical pixels; WPF handles the logical size on WM_DPICHANGED.
        var scale = GetDpiForWindow(_handle) / 96.0;
        if (scale <= 0) scale = 1;
        var width = (int)Math.Round(_window.ActualWidth * scale);
        var bounds = attachedToTop ? screen.Bounds : screen.WorkingArea;
        SetWindowPos(_handle, IntPtr.Zero, bounds.Left + Math.Max(0, (bounds.Width - width) / 2),
            bounds.Top + (attachedToTop ? 0 : (int)Math.Round(offset * scale)), 0, 0, 0x0015); // no size, z-order, or activation
    }

    public void SetClickThrough(bool enabled)
    {
        if (_handle == IntPtr.Zero || _clickThrough == enabled) return;
        var style = GetWindowLongPtr(_handle, -20).ToInt64();
        style = enabled ? style | 0x20 : style & ~0x20;
        SetWindowLongPtr(_handle, -20, new IntPtr(style));
        _clickThrough = enabled;
    }

    public bool IsForegroundFullscreenOnSelectedMonitor(string monitor)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _handle || foreground == GetShellWindow() ||
            !GetWindowRect(foreground, out var rect) || Screen(monitor) is not { } screen) return false;
        var className = new System.Text.StringBuilder(256);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;
        var bounds = screen.Bounds;
        return rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
        { HotkeyPressed?.Invoke(this, EventArgs.Empty); handled = true; }
        if (msg is 0x02E0 or 0x007E) _window?.Dispatcher.BeginInvoke(() => DisplayChanged?.Invoke(this, EventArgs.Empty));
        if (msg == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (msg == 0x0084 && _interactionBounds is not null && !IsPointerOver())
        { handled = true; return new IntPtr(-1); } // HTTRANSPARENT outside the animated surface
        return IntPtr.Zero;
    }

    public void Dispose()
    { if (HotkeyAvailable) UnregisterHotKey(_handle, HotkeyId); _source?.RemoveHook(WndProc); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, int modifiers, int key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int maximum);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
