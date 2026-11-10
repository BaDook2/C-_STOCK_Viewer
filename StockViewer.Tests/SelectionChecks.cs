using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static void ControlClick(QuoteWindow window)
    { window.ProcessMouseDown(MouseButton.Left, modifiers: KeyModifiers.Control); window.ProcessMouseUp(MouseButton.Left); }
    private static void GroupDrag(QuoteWindow window, int dx, int dy)
    {
        var cursor = new PixelPoint(window.Position.X + 20, window.Position.Y + 12);
        window.ProcessMouseDown(MouseButton.Left, screenPosition: cursor);
        window.ProcessMouseMove(new(cursor.X + dx, cursor.Y + dy), true, false);
        window.ProcessMouseUp(MouseButton.Left);
    }
    private static async Task SelectionTests()
    {
        using var selection = new PopupSelection();
        var windows = DemoMarketData.SampleStocks.Take(3).Select((stock, i) =>
        {
            var window = new QuoteWindow(stock, true, selection) { Position = new(100 + i * 320, 160) };
            window.ApplyQuote(new(stock.Code, 12345, i % 2 == 0 ? 1 : -1, DateTimeOffset.UtcNow));
            window.Show(); return window;
        }).ToArray();
        await Idle();
        try
        {
            ControlClick(windows[0]); ControlClick(windows[1]);
            Check(selection.Selected.Count == 2 && windows[0].IsSelected && windows[1].IsSelected && !windows[2].IsSelected,
                "Control-click selects independent price windows without changing an unrelated quote");
            var sizes = windows.Select(w => w.ClientSize).ToArray();
            ControlClick(windows[0]); await Idle(); ControlClick(windows[0]); await Idle();
            Check(windows.Select(w => w.ClientSize).SequenceEqual(sizes), "the thicker selection outline preserves the window and text layout sizes");
            ControlClick(windows[1]);
            Check(selection.Selected.Count == 1 && !windows[1].IsSelected && windows[0].IsSelected, "Control-click toggles just one selected quote off");
            windows[0].SetFixed(true); ControlClick(windows[1]);
            var positions = windows.Select(w => w.Position).ToArray();
            RightClick(windows[1], 2); await Idle();
            Check(windows.Take(2).All(w => !w.IsFixed && !w.Topmost && w.IsSelected && w.IsVisible)
                && windows.Select(w => w.Position).SequenceEqual(positions),
                "right-clicking a mixed fixed/free selection unpins every selected window without moving or closing one");
            RightClick(windows[0], 2); await Idle();
            Check(windows.Take(2).All(w => w.IsFixed && w.Topmost && w.IsVisible) && !windows[2].IsFixed,
                "right-clicking an entirely free selection pins it without touching other windows or double-closing");
            RightClick(windows[1], 2); GroupDrag(windows[1], 73, 48); await Idle();
            Check(windows[0].Position == new PixelPoint(positions[0].X + 73, positions[0].Y + 48)
                && windows[1].Position == new PixelPoint(positions[1].X + 73, positions[1].Y + 48) && windows[2].Position == positions[2],
                "dragging one selected price moves the group by exactly the same screen delta and keeps its spacing");
            positions = windows.Select(w => w.Position).ToArray(); windows[0].SetFixed(true);
            GroupDrag(windows[1], -120, 35); await Idle();
            Check(windows.Select(w => w.Position).SequenceEqual(positions), "a pinned participant prevents the whole group from splitting during a drag");
            RightClick(windows[1], 1); GroupDrag(windows[1], -36, -22); await Idle();
            Check(windows[0].Position == new PixelPoint(positions[0].X - 36, positions[0].Y - 22)
                && windows[1].Position == new PixelPoint(positions[1].X - 36, positions[1].Y - 22),
                "after group unpinning the same selection moves together in the opposite direction");

            foreach (var dark in new[] { false, true })
            {
                Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; await Idle();
                Check(windows.Take(2).All(w => BrushColor(Find<Border>(w, "Card").BorderBrush) == ThemeColor(w, "PopupOutlineBrush")
                    && Find<Border>(w, "Card").BorderThickness == new Thickness(2))
                    && BrushColor(Find<Border>(windows[2], "Card").BorderBrush) == ThemeColor(windows[2], "LineBrush"),
                    $"selected window outlines are two pixels thick in {(dark ? "dark" : "light")} mode");
                Check(windows.All(w => Find<Border>(w, "Card") is { Opacity: 1 } card
                    && BrushColor(card.Background) == ThemeColor(w, "SurfaceBrush"))
                    && BrushColor(Find<TextBlock>(windows[0], "PriceText").Foreground) == ThemeColor(windows[0], "RedBrush"),
                    "selection never tints price text or makes the background translucent");
                SaveImage(windows[0], dark ? "quote-selected-dark.png" : "quote-selected-light.png");
            }
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            GroupDrag(windows[2], 15, 12); await Idle();
            Check(selection.Selected.Count == 1 && windows[2].IsSelected && !windows[0].IsSelected && !windows[1].IsSelected,
                "an ordinary click on an unselected window starts a new single-window selection");

            foreach (var (window, i) in windows.Select((w, i) => (w, i)))
            { window.SetFixed(false); window.Position = new(100 + i * 320, 160); }
            await Idle();
            var first = PopupSelection.WindowBounds(windows[0]); var second = PopupSelection.WindowBounds(windows[1]);
            var from = new PixelPoint(first.X + 3, first.Y - 12);
            var end = new PixelPoint(second.Right - 3, second.Bottom - 3);
            selection.DesktopDown(from, false); selection.DesktopMove(end); selection.DesktopUp(end);
            Check(selection.Selected.Count == 2 && windows.Take(2).All(w => w.IsSelected) && !windows[2].IsSelected,
                "a desktop drag selects partially intersecting windows using their actual screen bounds");
            selection.DesktopMove(new(2000, 900));
            Check(selection.Selected.Count == 2, "desktop selection stops changing once the mouse button is released");
            selection.DesktopDown(end, false); selection.DesktopMove(from); selection.DesktopUp(from);
            Check(selection.Selected.Count == 2 && windows.Take(2).All(w => w.IsSelected), "desktop rectangles work when dragged from bottom-right to top-left");
            ControlClick(windows[2]); selection.DesktopDown(from, true); selection.DesktopUp(end);
            Check(selection.Selected.Count == 3, "Control plus a desktop rectangle preserves already selected prices");
            selection.DesktopDown(from, false); selection.DesktopMove(end); selection.DesktopMove(from); selection.DesktopUp(from);
            Check(selection.Selected.Count == 0, "shrinking a desktop rectangle back to its origin removes no-longer-intersecting prices");
            ControlClick(windows[0]); selection.DesktopDown(new(5, 5), false); selection.DesktopUp(new(5, 5));
            Check(selection.Selected.Count == 0 && windows.All(w => !w.IsSelected), "clicking empty desktop clears price selections without closing a window");
            Check(PopupSelection.RectangleBetween(new(-100, 300), new(-700, 100)) == new PixelRect(-700, 100, 600, 200),
                "desktop rectangles support negative monitor coordinates");
            ControlClick(windows[0]); ControlClick(windows[1]); windows[0].Close();
            Check(selection.Selected.Count == 1 && windows[1].IsSelected && !windows[0].IsSelected,
                "closing a selected window removes only that member and leaves no stale drag target");
        }
        finally { foreach (var window in windows) if (window.IsVisible) window.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Light; }

        var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        try
        {
            var first = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
            var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; await Idle();
            ControlClick(first); ControlClick(second);
            Check(main.SelectedPopups.Count == 2, "all application quote windows share the same selection controller");
            var replacement = (await main.OpenStockAsync(first.Stock))!; await Idle();
            Check(!first.IsVisible && !replacement.IsSelected && second.IsSelected && main.SelectedPopups.Count == 1,
                "reopening a symbol removes its previous selection without selecting its replacement");
            if (native && (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()))
            {
                await Until(() => main.Selection.DesktopObserverRunning);
                Check(true, "the native desktop mouse observer starts while real price windows are open");
            }
        }
        finally { main.Close(); }
        Check(main.SelectedPopups.Count == 0 && !main.Selection.DesktopObserverRunning, "closing the application releases selection state and its desktop observer");
    }

    private static async Task PointerSelectionTests()
    {
        var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        try
        {
            var first = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
            var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; await Idle();
            var point = new Point(30, 16);
            first.MouseDown(point, MouseButton.Left, RawInputModifiers.Control); first.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
            second.MouseDown(point, MouseButton.Left, RawInputModifiers.Control); second.MouseUp(point, MouseButton.Left, RawInputModifiers.Control); await Idle();
            Check(main.SelectedPopups.Count == 2 && first.IsSelected && second.IsSelected && !first.IsFixed && !second.IsFixed,
                "real Avalonia Control-click input selects two windows without accidentally toggling topmost");
            first.SetFixed(true);
            second.MouseDown(point, MouseButton.Right); second.MouseUp(point, MouseButton.Right); await Idle();
            Check(!first.IsFixed && !second.IsFixed && first.IsVisible && second.IsVisible, "raw right-click input unpins every selected native participant");
            var before = new[] { first.Position, second.Position };
            second.MouseDown(point, MouseButton.Left); second.MouseMove(new(70, 36), RawInputModifiers.LeftMouseButton); second.MouseUp(new(70, 36), MouseButton.Left); await Idle();
            Check(first.Position.X - before[0].X == second.Position.X - before[1].X
                && first.Position.Y - before[0].Y == second.Position.Y - before[1].Y && first.Position != before[0],
                "the raw pointer pipeline drags the entire group and releases its capture");
            before = [first.Position, second.Position];
            second.MouseDown(point, MouseButton.Left); second.MouseUp(new(55, 26), MouseButton.Left); await Idle();
            Check(first.Position.X - before[0].X == second.Position.X - before[1].X
                && first.Position.Y - before[0].Y == second.Position.Y - before[1].Y && first.Position != before[0],
                "a coalesced drag with no intermediate movement still moves the whole group on release");
            main.Selection.Clear(); first.MouseMove(point); await Idle();
            Check(BrushColor(Find<Border>(first, "Card").BorderBrush) == ThemeColor(first, "PopupOutlineBrush"),
                "unselected quote hover uses the same subtle outline as selection");
            first.MouseMove(new(-15, -15)); await Idle();
            Check(BrushColor(Find<Border>(first, "Card").BorderBrush) == ThemeColor(first, "LineBrush"), "leaving an unselected quote restores the normal outline");
        }
        finally { main.Close(); }
    }
}
