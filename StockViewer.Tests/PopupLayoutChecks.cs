using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static async Task PopupLayoutTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StockViewer-layout-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory);
        var main = MakeMain(store); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        Check(!main.SavePopupLayout() && store.LoadPopupLayout() is null, "an empty workspace cannot overwrite a saved layout");
        Check(!await main.RestorePopupLayoutAsync() && main.Popups.Count == 0, "missing layouts leave the workspace alone");
        foreach (var stock in DemoMarketData.SampleStocks.Take(10))
        {
            var popup = (await main.OpenStockAsync(stock))!;
            var i = Array.IndexOf(DemoMarketData.SampleStocks, stock);
            popup.Position = new(100 + i % 2 * 340, 130 + i / 2 * 54);
            popup.SetNameVisible(i % 3 != 0); popup.SetFixed(i % 2 == 0);
        }
        Check(main.SavePopupLayout(), "ten visible quotes can be saved with their window states");
        var saved = store.LoadPopupLayout()!;
        Check(saved.Popups.Length == 10 && saved.Popups.Count(p => p.IsFixed) == 5 && saved.Popups.Count(p => !p.ShowName) == 4,
            "disk layout stores independent topmost and name-visibility flags for every quote");
        main.Close(); await Idle();
        var reopened = MakeMain(new SettingsStore(directory));
        await reopened.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        var extra = (await reopened.OpenStockAsync(DemoMarketData.SampleStocks[12]))!;
        Check(await reopened.RestorePopupLayoutAsync(), "saved layouts restore after a complete application restart"); await Idle();
        Check(!extra.IsVisible && reopened.Popups.Count == 10, "successful loading restores the saved set and removes extra windows");
        Check(!reopened.Popups.Values.Any(p => Overlaps(WindowRect(p), new(reopened.Position,
            PixelSize.FromSize(reopened.ClientSize, reopened.RenderScaling)))),
            "restored pinned quotes cannot cover the search controls when a free screen slot exists");
        Check(saved.Popups.All(p => reopened.Popups[p.Stock.Code] is { IsVisible: true } popup
            && popup.Position == new PixelPoint(p.X, p.Y) && popup.IsFixed == p.IsFixed && popup.Topmost == p.IsFixed
            && popup.NameVisible == p.ShowName && popup.LastQuote is not null),
            "all ten positions, topmost flags, names and populated prices round-trip exactly");
        var reused = reopened.Popups.Values.ToArray();
        Check(await reopened.RestorePopupLayoutAsync() && reopened.Popups.Values.All(reused.Contains), "repeated loads reuse quotes without duplicates or unnecessary price requests");
        foreach (var invalid in new[] { "{", "null", "{\"Version\":99,\"Popups\":[]}",
            JsonSerializer.Serialize(saved with { Popups = [saved.Popups[0], saved.Popups[0]] }),
            "{\"Version\":1,\"Popups\":[null]}" })
        {
            File.WriteAllText(Path.Combine(directory, "popup-layout.json"), invalid);
            Check(!await reopened.RestorePopupLayoutAsync() && reopened.Popups.Count == 10, "invalid or incompatible layout never clears existing windows");
        }
        reopened.Close();
        Check(PopupPlacement.ClampToWorkArea(new(-9999, 9999), new(240, 32), new(-1600, 0, 1600, 900)) == new PixelPoint(-1600, 868),
            "a disconnected-monitor layout stays reachable in the current work area");

        store.SavePopupLayout(saved with { Popups = saved.Popups.Take(2).ToArray() });
        var partial = MakeMain(store); await partial.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        var retained = (await partial.OpenStockAsync(DemoMarketData.SampleStocks[12]))!;
        var slow = new ControlledQuotes(); await partial.UseServiceAsync(slow, DemoMarketData.SampleStocks);
        var restoring = partial.RestorePopupLayoutAsync(); await Until(() => slow.Requests.Count == 3);
        Check(partial.Popups.Count == 1 && !Find<Button>(partial, "LoadLayoutButton").IsEnabled,
            "restoring waits for populated prices and prevents simultaneous layout loads");
        slow.Requests.Single(r => r.Code == saved.Popups[0].Stock.Code).Reply.TrySetResult(new(saved.Popups[0].Stock.Code, 12000, 1, DateTimeOffset.UtcNow));
        Check(!await restoring && retained.IsVisible && partial.Popups.Count == 2 && partial.PendingQuoteCount == 0,
            "a partial feed timeout keeps existing windows and successfully restored quotes");
        Check(partial.DataDelayNotice is null && partial.OwnedWindows.Count == 0,
            "a layout restoration timeout does not create a data-delay or layout notice popup");
        Check(store.LoadPopupLayout()!.Popups.Length == 2 && Find<Button>(partial, "LoadLayoutButton").IsEnabled,
            "failed restoration preserves the disk snapshot and enables retry");
        partial.Close(); foreach (var request in slow.Requests) request.Reply.TrySetCanceled();
    }
    private static async Task MultipleTopmostTests()
    {
        const int windowCount = 20;
        var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        try
        {
            var quotes = new List<QuoteWindow>();
            foreach (var stock in DemoMarketData.SampleStocks.Take(windowCount))
            {
                var quote = (await main.OpenStockAsync(stock))!; quotes.Add(quote);
                // A native double-click counter can carry across nearby windows.
                // A first right click in this window must pin, never close it.
                RightClick(quote, 2);
            }
            await Idle();
            Check(quotes.All(q => q.IsVisible && q.IsFixed && q.Topmost), "a first right click with an inherited click count pins all twenty quotes");
            foreach (var dark in new[] { false, true })
            {
                Application.Current!.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
                await Idle();
                foreach (var (quote, i) in quotes.Select((q, i) => (q, i)))
                {
                    quote.SetConnection(new("temporary feed interruption", i % 2 == 0, Code: quote.Stock.Code));
                    if (i % 3 == 0) quote.ShowError("시세 조회 실패");
                }
                Check(quotes.All(q => Find<Border>(q, "Card") is { Opacity: 1 } card
                    && card.Background is Avalonia.Media.ISolidColorBrush brush && brush.Color.A == 255 && q.LastQuote is not null),
                    $"all twenty quote backgrounds stay fully opaque during feed errors in {(dark ? "dark" : "light")} mode");
                SaveImage(quotes[0], dark ? "quote-feed-error-dark.png" : "quote-feed-error-light.png");
            }
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            foreach (var quote in quotes) { quote.Activate(); quote.SetNameVisible(!quote.NameVisible); }
            main.Activate(); await Idle();
            Check(quotes.All(q => q.IsFixed && q.Topmost), "activation and content resizing keep every quote pinned independently");
            var first = quotes[0]; first.SetFixed(false); first.Position = new(800, 130); first.SetFixed(true);
            var position = first.Position;
            var replacement = (await main.OpenStockAsync(first.Stock))!; quotes[0] = replacement;
            Check(!first.IsVisible && !replacement.IsFixed && !replacement.Topmost && replacement.Position != position
                && quotes.Skip(1).All(q => q.IsFixed),
                "searching an already pinned symbol unpins and repositions only its replacement");
            replacement.SetFixed(true);
            if (native && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            {
                Console.WriteLine("Native quote handle: " + replacement.TryGetPlatformHandle()!.HandleDescriptor);
                Check(quotes.All(q => PopupTopmost.Read(q) == true), "all twenty real native windows are topmost, independently of the managed flags");
                foreach (var quote in quotes)
                {
                    if (OperatingSystem.IsWindows()) SetWindowPos(quote.TryGetPlatformHandle()!.Handle, new(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                    else
                    {
                        var h = quote.TryGetPlatformHandle()!;
                        var window = h.HandleDescriptor == "NSView" ? ObjcSend(h.Handle, ObjcSelector("window")) : h.Handle;
                        ObjcSetLevel(window, ObjcSelector("setLevel:"), 0);
                    }
                }
                await Until(() => quotes.All(q => PopupTopmost.Read(q) == true));
                Check(true, "native demotion of multiple pinned windows is repaired for the entire group");
                quotes[windowCount - 1].SetFixed(false); main.Activate(); await Idle();
                Check(PopupTopmost.Read(quotes[windowCount - 1]) == false && quotes.Take(windowCount - 1).All(q => PopupTopmost.Read(q) == true),
                    "unpinning one native window leaves the other nineteen above normal windows");
                quotes[windowCount - 1].SetFixed(true);
            }
            var last = quotes[windowCount - 1]; RightClick(last, 1); RightClick(last, 2); await Idle();
            Check(!last.IsVisible && quotes.Take(windowCount - 1).All(q => q.IsFixed), "double right click closes only its own quote and keeps the other pins");
        }
        finally { main.Close(); }
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr ObjcSelector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr ObjcSend(IntPtr target, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void ObjcSetLevel(IntPtr target, IntPtr selector, nint level);
}
