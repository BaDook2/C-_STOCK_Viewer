using Avalonia;
using Avalonia.Controls;

namespace StockViewer.Desktop;

public static class PopupPlacement
{
    public static void BelowSearch(Window popup, Window search) => BelowAnchor(popup, search);
    public static void BelowQuote(Window search, Window quote) => BelowAnchor(search, quote);
    public static PixelPoint BelowAnchor(PixelRect anchor, PixelSize popup, PixelRect workArea, double scaling = 1)
    {
        var gap = (int)Math.Round(4 * scaling);
        var left = anchor.X + (anchor.Width - popup.Width) / 2;
        var top = anchor.Bottom + gap;
        if (top + popup.Height > workArea.Bottom) top = anchor.Y - popup.Height - gap;
        return new(Math.Clamp(left, workArea.X, Math.Max(workArea.X, workArea.Right - popup.Width)),
            Math.Clamp(top, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - popup.Height)));
    }
    private static void BelowAnchor(Window popup, Window anchor)
    {
        popup.Measure(Size.Infinity);
        var sourceScale = anchor.RenderScaling;
        var monitor = anchor.Screens.ScreenFromWindow(anchor) ?? anchor.Screens.Primary;
        var scale = monitor?.Scaling ?? sourceScale;
        var width = popup.Bounds.Width > 0 ? popup.Bounds.Width : popup.DesiredSize.Width;
        var height = popup.Bounds.Height > 0 ? popup.Bounds.Height : popup.DesiredSize.Height;
        var anchorSize = PixelSize.FromSize(anchor.Bounds.Size, sourceScale);
        var bounds = new PixelRect(anchor.Position, anchorSize);
        var popupSize = PixelSize.FromSize(new(width, height), scale);
        var workArea = monitor?.WorkingArea ?? new PixelRect(anchor.Position.X - 2000, anchor.Position.Y - 2000, 4000, 4000);
        popup.Position = BelowAnchor(bounds, popupSize, workArea, scale);
    }
}
