using Avalonia;

namespace StockViewer.Desktop;

internal sealed class PopupSelection(bool observeDesktop = false) : IDisposable
{
    private readonly HashSet<QuoteWindow> windows = [], selected = [];
    private Dictionary<QuoteWindow, PixelPoint>? moving;
    private QuoteWindow? moveAnchor;
    private DesktopMarqueeObserver? observer;
    private HashSet<QuoteWindow> rectangleBase = [];
    private PixelPoint rectangleStart;
    private bool rectanglePending, rectangleDragging, disposed;
    public IReadOnlyCollection<QuoteWindow> Selected => selected;
    internal bool DesktopObserverRunning => observer?.IsRunning == true;

    public void Register(QuoteWindow window)
    {
        if (disposed || !windows.Add(window)) return;
        if (observeDesktop && observer is null && window.TryGetPlatformHandle()?.HandleDescriptor is "HWND" or "NSWindow" or "NSView")
            observer = DesktopMarqueeObserver.Start(this);
    }
    public void Remove(QuoteWindow window)
    {
        windows.Remove(window); selected.Remove(window); rectangleBase.Remove(window);
        window.SetSelected(false);
        moving?.Remove(window);
        if (ReferenceEquals(moveAnchor, window)) CancelMove();
        if (windows.Count == 0) { observer?.Dispose(); observer = null; rectanglePending = false; }
    }
    public void Toggle(QuoteWindow window)
    {
        if (!windows.Contains(window) || !window.IsVisible) return;
        SetSelected(window, !selected.Contains(window));
    }
    private void SetSelected(QuoteWindow window, bool enabled)
    {
        if (enabled) selected.Add(window); else selected.Remove(window);
        window.SetSelected(enabled);
    }
    public void Clear()
    {
        CancelMove();
        foreach (var window in selected.ToArray()) SetSelected(window, false);
    }
    public void BeginMove(QuoteWindow anchor)
    {
        if (!selected.Contains(anchor)) { Clear(); SetSelected(anchor, true); }
        moveAnchor = anchor;
        moving = selected.Where(w => w.IsVisible).ToDictionary(w => w, w => w.Position);
    }
    public void Move(QuoteWindow anchor, int dx, int dy)
    {
        if (!ReferenceEquals(moveAnchor, anchor) || moving is null || moving.Keys.Any(w => w.IsFixed)) return;
        foreach (var (window, origin) in moving)
            if (window.IsVisible) window.Position = new(origin.X + dx, origin.Y + dy);
    }
    public void CancelMove() { moving = null; moveAnchor = null; }
    public void CloseSelection(QuoteWindow window)
    {
        CancelMove();
        var targets = selected.Contains(window) ? selected.ToArray() : [window];
        foreach (var target in targets) if (target.IsVisible) target.Close();
    }
    public void RightClick(QuoteWindow window)
    {
        CancelMove();
        if (!selected.Contains(window)) Clear();
        var targets = selected.Contains(window) ? selected.ToArray() : [window];
        var enable = !targets.Any(w => w.IsFixed);
        foreach (var target in targets) target.SetFixed(enable);
    }
    internal void DesktopDown(PixelPoint point, bool additive)
    {
        if (disposed) return;
        CancelMove(); rectangleStart = point; rectanglePending = true; rectangleDragging = false;
        rectangleBase = additive ? selected.ToHashSet() : [];
        if (!additive) Clear();
    }
    internal void DesktopMove(PixelPoint point)
    {
        if (!rectanglePending || disposed) return;
        if (!rectangleDragging && Math.Abs((long)point.X - rectangleStart.X) < 4 && Math.Abs((long)point.Y - rectangleStart.Y) < 4) return;
        rectangleDragging = true;
        var bounds = RectangleBetween(rectangleStart, point);
        foreach (var window in windows.ToArray())
            SetSelected(window, window.IsVisible && (rectangleBase.Contains(window) || Overlaps(bounds, WindowBounds(window))));
    }
    internal void DesktopUp(PixelPoint point)
    { DesktopMove(point); rectanglePending = false; rectangleBase.Clear(); }
    internal static PixelRect RectangleBetween(PixelPoint a, PixelPoint b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(1, Math.Abs(a.X - b.X)), Math.Max(1, Math.Abs(a.Y - b.Y)));
    internal static PixelRect WindowBounds(QuoteWindow window)
    {
        var first = window.PointToScreen(default);
        var last = window.PointToScreen(new(window.ClientSize.Width, window.ClientSize.Height));
        return RectangleBetween(first, last);
    }
    private static bool Overlaps(PixelRect a, PixelRect b) => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; observer?.Dispose(); observer = null; rectanglePending = false;
        Clear(); windows.Clear(); rectangleBase.Clear();
    }
}
