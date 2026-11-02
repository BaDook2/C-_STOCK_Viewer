using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StockViewer.App;

public static class PopupPlacement
{
    /// <summary>Place below the search window, or above when the monitor has no room below.</summary>
    public static void BelowSearch(Window popup, Window search)
    {
        popup.UpdateLayout();
        var width = popup.ActualWidth > 0 ? popup.ActualWidth : popup.DesiredSize.Width;
        var height = popup.ActualHeight > 0 ? popup.ActualHeight : popup.DesiredSize.Height;
        var left = search.Left + (search.ActualWidth - width) / 2;
        var top = search.Top + search.ActualHeight + 4;
        var handle = new WindowInteropHelper(search).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info) && PresentationSource.FromVisual(search) is HwndSource source && source.CompositionTarget is { } target)
        {
            var matrix = target.TransformFromDevice;
            var start = matrix.Transform(new Point(info.Work.Left, info.Work.Top));
            var end = matrix.Transform(new Point(info.Work.Right, info.Work.Bottom));
            if (top + height > end.Y) top = search.Top - height - 4;
            left = Math.Clamp(left, start.X, Math.Max(start.X, end.X - width));
            top = Math.Clamp(top, start.Y, Math.Max(start.Y, end.Y - height));
        }
        popup.Left = left; popup.Top = top;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
