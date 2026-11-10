using Avalonia;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static bool Overlaps(PixelRect a, PixelRect b) => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
    private static PixelRect WindowRect(QuoteWindow window) => new(window.Position, PixelSize.FromSize(window.ClientSize, window.RenderScaling));
    private static async Task PlacementTests()
    {
        var area = new PixelRect(0, 0, 800, 600); var size = new PixelSize(160, 32); var preferred = new PixelPoint(200, 200);
        Check(PopupPlacement.FindFreePosition(preferred, size, area, []) == preferred, "an empty popup position remains unchanged");
        Check(PopupPlacement.FindFreePosition(preferred, size, area, [new(200, 200, 180, 32)]) == new PixelPoint(200, 236),
            "a new quote is placed below an occupied position with a four-DIP gap");
        Check(PopupPlacement.FindFreePosition(preferred, size, area, [new(200, 200, 180, 32), new(180, 236, 220, 32)]) == new PixelPoint(200, 272),
            "stacking skips all overlapping rows even when their widths differ");
        Check(PopupPlacement.FindFreePosition(new(200, 568), size, area, [new(200, 568, 180, 32)]) == new PixelPoint(200, 532),
            "a bottom-edge quote stacks above the existing popup instead of leaving the screen");
        Check(PopupPlacement.FindFreePosition(new(200, 568), size, area, [new(200, 568, 180, 32), new(180, 500, 286, 64)]) == new PixelPoint(200, 464),
            "upward stacking also avoids covering the search window");
        Check(PopupPlacement.FindFreePosition(preferred, size, area, [new(350, 200, 200, 32)]) == new PixelPoint(200, 236),
            "partial horizontal overlaps are detected rather than comparing only window origins");
        Check(PopupPlacement.FindFreePosition(preferred, size, area, [new(500, 200, 180, 32)]) == preferred,
            "an unrelated quote in another column does not shift the new popup");
        Check(PopupPlacement.FindFreePosition(new(-1500, 200), new(240, 48), new(-1600, 0, 1600, 900), [new(-1500, 200, 240, 48)], 1.5)
            == new PixelPoint(-1500, 254), "stacking uses physical pixels and a six-pixel gap at 150-percent DPI on a negative-coordinate monitor");
        Check(PopupPlacement.FindFreePosition(new(0, 0), new(100, 32), new(0, 0, 600, 120),
            [new(0, 0, 100, 32), new(0, 36, 100, 32), new(0, 72, 100, 32)]) == new PixelPoint(104, 0),
            "a filled vertical column uses the next free column on the same monitor");
        var crowded = Enumerable.Range(0, 12).Select(i => new PixelRect(200, 100 + i * 36, 200, 32)).ToArray();
        var position = PopupPlacement.FindFreePosition(new(200, 100), size, area, crowded);
        Check(!crowded.Any(r => Overlaps(new(position, size), r)) && position.Y >= area.Y && position.Y + size.Height <= area.Bottom,
            "a long stack finds a visible free position without overlapping any existing quote");
        Check(PopupPlacement.FindFreePosition(new(0, 0), new(100, 32), new(0, 0, 100, 32), [new(0, 0, 100, 32)]) == new PixelPoint(0, 0),
            "a physically full work area terminates deterministically without placing the window off-screen");

        var main = MakeMain();
        try
        {
            await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks); await Idle();
            var first = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!; await Idle();
            var original = first.Position; first.SetFixed(true);
            var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; await Idle();
            var secondPosition = second.Position;
            var third = (await main.OpenStockAsync(DemoMarketData.SampleStocks[2]))!; await Idle();
            Check(!Overlaps(WindowRect(first), WindowRect(second)) && !Overlaps(WindowRect(first), WindowRect(third))
                && !Overlaps(WindowRect(second), WindowRect(third)), "opening three actual quote windows never covers the earlier quotes");
            Check(first.Position == original && first.IsFixed && second.Position == secondPosition,
                "placement moves only the new window and preserves existing fixed and free windows");
            Check(second.Position.Y >= WindowRect(first).Bottom + (int)Math.Round(4 * second.RenderScaling)
                && third.Position.Y >= WindowRect(second).Bottom + (int)Math.Round(4 * third.RenderScaling),
                "actual new quote windows stack downward while space remains");
            var replacement = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!; await Idle();
            Check(!first.IsVisible && replacement.Position.Y == original.Y && !Overlaps(WindowRect(replacement), WindowRect(second)),
                "reopening the same symbol reuses its vacated slot rather than counting the old window as occupied");
        }
        finally { foreach (var popup in main.Popups.Values.ToArray()) popup.Close(); main.Close(); }

        var pending = MakeMain(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PublicHttpFixture { HoldUs = release };
        await using var service = new PublicMarketData(handler);
        try
        {
            var apple = new Stock("US:AAPL.O", "애플", "NASDAQ", "AAPL"); var spy = new Stock("US:SPY", "SPY", "AMEX", "SPY");
            await pending.UseServiceAsync(service, [apple, spy]); await Idle();
            var openingApple = pending.OpenStockAsync(apple);
            await handler.UsStarted.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Idle();
            var first = pending.Popups[apple.Code]; first.Position = new(first.Position.X, first.Position.Y + 8);
            var moved = first.Position;
            var openingSpy = pending.OpenStockAsync(spy);
            await Until(() => pending.Popups.Count == 2); await Idle();
            Check(!Overlaps(WindowRect(first), WindowRect(pending.Popups[spy.Code])),
                "quotes still loading their first price reserve their screen space");
            release.TrySetResult(); await Task.WhenAll(openingApple, openingSpy); await Idle();
            Check(first.Position == moved, "a delayed first price does not snap a manually moved quote back to the search window");
            Check(!Overlaps(WindowRect(first), WindowRect(pending.Popups[spy.Code])),
                "first-price content growth and final placement keep pending quote windows separate");
        }
        finally
        {
            release.TrySetResult(); foreach (var popup in pending.Popups.Values.ToArray()) popup.Close(); pending.Close();
        }
    }
}
