using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace StockViewer.Desktop;

// Keep every quote's native pin state in sync, without activating or
// repeatedly raising healthy windows. Avalonia's property alone is cached and
// cannot repair a window that the native window manager has demoted.
internal static class PopupTopmost
{
    private static readonly HashSet<QuoteWindow> windows = [];
    private static readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static bool queued;
    static PopupTopmost() => timer.Tick += (_, _) => EnsureAll();
    public static void Update(QuoteWindow window)
    {
        if (window.IsVisible) windows.Add(window); else windows.Remove(window);
        Ensure(window, window.IsFixed);
        if (windows.Count > 0) timer.Start(); else timer.Stop();
        QueueCheck();
    }
    public static void Remove(QuoteWindow window)
    { windows.Remove(window); if (windows.Count == 0) timer.Stop(); }
    public static void QueueCheck()
    {
        if (queued || windows.Count == 0) return;
        queued = true;
        Dispatcher.UIThread.Post(() => { queued = false; EnsureAll(); }, DispatcherPriority.Background);
    }
    private static void EnsureAll()
    { foreach (var window in windows.ToArray()) if (window.IsVisible) Ensure(window, window.IsFixed); }
    private static void Ensure(Window window, bool enabled)
    {
        if (!window.IsVisible || Read(window) is not { } actual || actual == enabled) return;
        var handle = window.TryGetPlatformHandle()!;
        if (OperatingSystem.IsWindows())
            SetWindowPos(handle.Handle, enabled ? new(-1) : new(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
        else if (OperatingSystem.IsMacOS())
            SendLevel(MacWindow(window), Selector("setLevel:"), enabled ? 3 : 0);
    }
    internal static bool? Read(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return null;
        if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND")
            return ReadWindowsTopmost(handle.Handle);
        if (OperatingSystem.IsMacOS() && MacWindow(window) is var native && native != IntPtr.Zero)
            return Send(native, Selector("level")).ToInt64() >= 3;
        return null;
    }
    internal static bool ReadWindowsTopmost(IntPtr handle) => (GetWindowLongPtr(handle, -20).ToInt64() & 0x00000008) != 0;
    private static IntPtr MacWindow(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        return handle?.HandleDescriptor switch
        {
            "NSWindow" => handle.Handle,
            "NSView" => Send(handle.Handle, Selector("window")),
            _ => IntPtr.Zero
        };
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Selector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendLevel(IntPtr target, IntPtr selector, nint level);
}
