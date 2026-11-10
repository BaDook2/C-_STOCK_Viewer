using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static async Task NativeWindowChecks()
    {
        if (OperatingSystem.IsMacOS()) await MacDesktopHitChecks();
        if (!OperatingSystem.IsWindows()) return;
        var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        var quote = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!; await Idle();
        var searchHandle = main.TryGetPlatformHandle()!.Handle;
        var quoteHandle = quote.TryGetPlatformHandle()!.Handle;
        Check(IsWindow(searchHandle) && IsWindow(quoteHandle), "Avalonia creates real independent Windows handles");
        Check(main.ActualTransparencyLevel == WindowTransparencyLevel.Transparent && quote.ActualTransparencyLevel == WindowTransparencyLevel.Transparent,
            "native search and quote use real transparent window backgrounds");
        quote.SetFixed(true); var before = quote.Position;
        SetWindowPos(quoteHandle, IntPtr.Zero, before.X + 60, before.Y + 40, 0, 0, 0x0001 | 0x0004 | 0x0010); await Idle();
        Check(quote.Position == before, "Win32 hook blocks external/system moves while fixed");
        var result = SendMessage(quoteHandle, 0x007B, quoteHandle, new IntPtr(-1));
        Check(result == IntPtr.Zero && quote.IsVisible, "the quote consumes WM_CONTEXTMENU without opening a native menu");
        HideSearch(main); await Idle(); Check(!IsWindowVisible(searchHandle) && IsWindowVisible(quoteHandle), "native X hides only search HWND");
        Chord(quote); await Idle(); Check(IsWindowVisible(searchHandle) && main.TryGetPlatformHandle()!.Handle == searchHandle, "native chord reuses the hidden search HWND");
        HideSearch(main); RightClick(quote, 1); RightClick(quote, 2); await Idle();
        await Until(() => !IsWindow(searchHandle) && !IsWindow(quoteHandle));
        Check(true, "last quote destroys native windows and cleans up the session");
        var taskbar = MakeMain(); await taskbar.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        var first = (await taskbar.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
        var second = (await taskbar.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; first.SetFixed(true); await Idle();
        var taskbarHandle = taskbar.TryGetPlatformHandle()!.Handle;
        var firstHandle = first.TryGetPlatformHandle()!.Handle; var secondHandle = second.TryGetPlatformHandle()!.Handle;
        SendMessage(taskbarHandle, 0x0112, new IntPtr(0xF060), IntPtr.Zero); // WM_SYSCOMMAND / SC_CLOSE, like the taskbar's Close window.
        await Until(() => !IsWindow(taskbarHandle) && !IsWindow(firstHandle) && !IsWindow(secondHandle));
        Check(true, "Windows taskbar system-close destroys search and every fixed or free quote HWND");
    }
    private static async Task MacDesktopHitChecks()
    {
        var main = MakeMain();
        try
        {
            var screen = main.Screens.Primary!;
            var area = screen.WorkingArea;
            PixelPoint? desktop = null;
            for (var y = area.Bottom - 70; y > area.Y + 40 && desktop is null; y -= 100)
            for (var x = area.Right - 80; x > area.X + 40; x -= 100)
            {
                var point = new PixelPoint(x, y);
                if (DesktopMarqueeObserver.IsMacDesktopPoint(point)) { desktop = point; break; }
            }
            Check(desktop.HasValue, "macOS finds real desktop pixels despite the screen-sized transparent Dock window");
            var location = desktop!.Value;
            var overlay = new Window
            {
                Width = 120, Height = 90, Topmost = true, ShowInTaskbar = false,
                WindowDecorations = WindowDecorations.None, WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new(location.X - 50, location.Y - 30),
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Background = Avalonia.Media.Brushes.Transparent
            };
            try
            {
                overlay.Show(); await Idle();
                var handle = overlay.TryGetPlatformHandle()!;
                var nativeWindow = handle.HandleDescriptor == "NSView" ? ObjcSend(handle.Handle, ObjcSelector("window")) : handle.Handle;
                SetObjcFlag(nativeWindow, ObjcSelector("setIgnoresMouseEvents:"), true);
                await Idle();
                Check(DesktopMarqueeObserver.IsMacDesktopPoint(location), "desktop hit-testing ignores a full-opacity native window that passes mouse input through");
                SetObjcFlag(nativeWindow, ObjcSelector("setIgnoresMouseEvents:"), false);
                overlay.Background = Avalonia.Media.Brushes.White; await Idle();
                Check(!DesktopMarqueeObserver.IsMacDesktopPoint(location), "desktop detection excludes ordinary clickable application windows");
            }
            finally { overlay.Close(); }
            await Idle();
            Check(DesktopMarqueeObserver.IsMacDesktopPoint(location), "closing an application window exposes the real desktop to selection again");
        }
        finally { main.Close(); }
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetObjcFlag(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
