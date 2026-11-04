using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StockViewer.App;

public static class PopupPlacement
{
    /// <summary>Place below the search window, or above when the monitor has no room below.</summary>
    public static void BelowSearch(Window popup, Window search) => BelowAnchor(popup, search);
    public static void BelowQuote(Window search, Window quote) => BelowAnchor(search, quote);
    public static Point BelowAnchor(Rect anchor, Size popup, Rect workArea)
    {
        const double gap = 4;
        var left = anchor.Left + (anchor.Width - popup.Width) / 2;
        var top = anchor.Bottom + gap;
        if (top + popup.Height > workArea.Bottom) top = anchor.Top - popup.Height - gap;
        return new(Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - popup.Width)),
            Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - popup.Height)));
    }
    private static void BelowAnchor(Window popup, Window anchor)
    {
        popup.UpdateLayout();
        var width = popup.ActualWidth > 0 ? popup.ActualWidth : popup.DesiredSize.Width;
        var height = popup.ActualHeight > 0 ? popup.ActualHeight : popup.DesiredSize.Height;
        var bounds = new System.Windows.Rect(anchor.Left, anchor.Top, anchor.ActualWidth, anchor.ActualHeight);
        var position = new Point(bounds.Left + (bounds.Width - width) / 2, bounds.Bottom + 4);
        var handle = new WindowInteropHelper(anchor).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info) && PresentationSource.FromVisual(anchor) is HwndSource source && source.CompositionTarget is { } target)
        {
            var matrix = target.TransformFromDevice;
            var start = matrix.Transform(new Point(info.Work.Left, info.Work.Top));
            var end = matrix.Transform(new Point(info.Work.Right, info.Work.Bottom));
            position = BelowAnchor(bounds, new(width, height), new System.Windows.Rect(start, end));
        }
        popup.Left = position.X; popup.Top = position.Y;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
