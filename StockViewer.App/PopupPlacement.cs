using Avalonia;
using Avalonia.Controls;

namespace StockViewer.Desktop;

public static class PopupPlacement
{
    public static void BelowSearch(Window popup, Window search, IEnumerable<Window>? occupied = null) => BelowAnchor(popup, search, occupied);
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
    public static PixelPoint FindFreePosition(PixelPoint preferred, PixelSize size, PixelRect workArea,
        IEnumerable<PixelRect> occupied, double scaling = 1)
    {
        var gap = Math.Max(1, (int)Math.Round(4 * scaling));
        var maxX = Math.Max(workArea.X, workArea.Right - size.Width);
        var maxY = Math.Max(workArea.Y, workArea.Bottom - size.Height);
        preferred = new(Math.Clamp(preferred.X, workArea.X, maxX), Math.Clamp(preferred.Y, workArea.Y, maxY));
        var obstacles = occupied.Where(r => r.Width > 0 && r.Height > 0).ToArray();
        // Keep the original column first. Extra columns are a last resort when
        // the monitor has no vertical slot left, rather than covering a quote.
        var columns = new[] { preferred.X }.Concat(obstacles.SelectMany(r => new[] { r.Right + gap, r.X - size.Width - gap }))
            .Append(workArea.X).Append(maxX).Where(x => x >= workArea.X && x <= maxX)
            .Distinct().OrderBy(x => Math.Abs((long)x - preferred.X));
        foreach (var x in columns)
        {
            var blocked = obstacles.Where(r => x < r.Right + gap && x + size.Width + gap > r.X).ToArray();
            var y = preferred.Y;
            foreach (var r in blocked.OrderBy(r => r.Y))
                if (y < r.Bottom + gap && y + size.Height + gap > r.Y) y = r.Bottom + gap;
            if (y >= workArea.Y && y + size.Height <= workArea.Bottom && x + size.Width <= workArea.Right) return new(x, y);
            y = preferred.Y;
            foreach (var r in blocked.OrderByDescending(r => r.Bottom))
                if (y < r.Bottom + gap && y + size.Height + gap > r.Y) y = r.Y - size.Height - gap;
            if (y >= workArea.Y && y + size.Height <= workArea.Bottom && x + size.Width <= workArea.Right) return new(x, y);
        }
        // A completely filled (or smaller-than-popup) work area cannot provide a
        // non-overlapping slot. Stay on the chosen monitor in that case.
        return preferred;
    }
    private static Size PlacementSize(Window window) => new(
        Math.Max(window.ClientSize.Width, window.DesiredSize.Width),
        Math.Max(window.ClientSize.Height, window.DesiredSize.Height));
    private static void BelowAnchor(Window popup, Window anchor, IEnumerable<Window>? occupied = null)
    {
        popup.Measure(Size.Infinity);
        var sourceScale = anchor.RenderScaling;
        var monitor = anchor.Screens.ScreenFromWindow(anchor) ?? anchor.Screens.Primary;
        var scale = monitor?.Scaling ?? sourceScale;
        // Include pending content growth without relying on stale native Bounds.
        var anchorSize = PixelSize.FromSize(PlacementSize(anchor), sourceScale);
        var bounds = new PixelRect(anchor.Position, anchorSize);
        var popupSize = PixelSize.FromSize(PlacementSize(popup), scale);
        var workArea = monitor?.WorkingArea ?? new PixelRect(anchor.Position.X - 2000, anchor.Position.Y - 2000, 4000, 4000);
        var preferred = BelowAnchor(bounds, popupSize, workArea, scale);
        var obstacles = (occupied ?? []).Where(w => !ReferenceEquals(w, popup) && !ReferenceEquals(w, anchor)
            && w.IsVisible && w.WindowState != WindowState.Minimized)
            .Select(w => new PixelRect(w.Position, PixelSize.FromSize(PlacementSize(w), w.RenderScaling))).Append(bounds);
        popup.Position = FindFreePosition(preferred, popupSize, workArea, obstacles, scale);
    }
}
