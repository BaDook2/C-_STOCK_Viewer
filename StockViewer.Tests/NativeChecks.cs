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
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
