using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StockViewer.App;

public static class PopupPlacement
{
    /// <summary>Center on the search window; constrain to its monitor's available desktop.</summary>
    public static void Center(Window popup, Window search)
    {
        popup.UpdateLayout();
        var width = popup.ActualWidth > 0 ? popup.ActualWidth : popup.DesiredSize.Width;
        var height = popup.ActualHeight > 0 ? popup.ActualHeight : popup.DesiredSize.Height;
        var left = search.Left + (search.ActualWidth - width) / 2;
        var top = search.Top + (search.ActualHeight - height) / 2;
        var handle = new WindowInteropHelper(search).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info) && PresentationSource.FromVisual(search) is HwndSource source && source.CompositionTarget is { } target)
        {
            var matrix = target.TransformFromDevice;
            var start = matrix.Transform(new Point(info.Work.Left, info.Work.Top));
            var end = matrix.Transform(new Point(info.Work.Right, info.Work.Bottom));
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
