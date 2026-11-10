using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static int passed;
    private static bool native;
    [STAThread]
    public static int Main(string[] args)
    {
        native = args.Contains("--native");
        var builder = AppBuilder.Configure<App>();
        if (native) builder.UsePlatformDetect();
        else builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        builder.SetupWithoutStarting();
        using var finish = new CancellationTokenSource();
        var exitCode = 0;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (args.Contains("--pointer-only")) await PointerPipelineTests();
                else if (args.Contains("--public-smoke")) await LivePublicSmoke();
                else
                {
                    SettingsMigrationTests();
                    if (!args.Contains("--ui-only")) { await CoreTests(); await TransportTests(); await PublicDataTests(); ExtendedSessionTests(); await SessionPollingTests(); await UnitedStatesDataTests(); await DayMarketTests(); await FastPollingTests(); }
                    await LoadingPopupTests(); await LoadingDeadlineTests(); await NativeCloseTests(); await PlacementTests(); await PopupLayoutTests(); await LayoutFeedbackTests(); await TaskbarPersistenceTests(); await MultipleTopmostTests(); await SelectionTests(); await GroupCloseTests(); await ThemeAndSessionTests(); await UiTests(); await GestureTests(); await SearchTests(); await LifetimeTests();
                    if (!native) await PointerPipelineTests();
                    if (native) await NativeWindowChecks();
                }
                Console.WriteLine($"PASS: {passed} assertions ({(native ? "native desktop" : "headless")}); no brokerage orders.");
            }
            catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
            finally { finish.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(finish.Token);
        return exitCode;
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
    private static async Task Throws<T>(Func<Task> action, string label) where T : Exception
    { try { await action(); } catch (T) { Check(true, label); return; } throw new Exception("FAIL: " + label); }
    private static JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
    private static async Task Until(Func<bool> condition)
    { using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(7)); while (!condition()) await Task.Delay(15, limit.Token); }
    private static async Task Idle() { await Task.Delay(90); }
    private static void SettingsMigrationTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "StockViewer-migration-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "StockViewer_avalonia");
        var destination = Path.Combine(root, "StockViewer");
        var old = new SettingsStore(legacy);
        old.SaveDisplayName(false); old.SaveCatalog([DemoMarketData.SampleStocks[0]]);
        File.WriteAllText(Path.Combine(legacy, "kiwoom.protected"), "test-only legacy credentials");
        var migrated = new SettingsStore(destination, legacy);
        Check(!migrated.LoadDisplayName(), "rename imports a missing display preference from the old Avalonia folder");
        Check(migrated.LoadCatalog().Single().Code == "005930", "rename imports a missing stock catalog");
        Check(!File.Exists(Path.Combine(destination, "kiwoom.protected")), "settings migration never copies credentials");
        migrated.SaveDisplayName(true); migrated.SaveCatalog([DemoMarketData.SampleStocks[1]]);
        var reopened = new SettingsStore(destination, legacy);
        Check(reopened.LoadDisplayName() && reopened.LoadCatalog().Single().Code == "000660", "existing StockViewer preferences are never overwritten by migration");
        Check(File.Exists(Path.Combine(legacy, "display-name.txt")), "migration keeps the old user's settings files intact");
    }
    private static T Find<T>(Control root, string name) where T : Control => root.FindControl<T>(name) ?? throw new Exception("Missing control " + name);
    private static MainWindow MakeMain(SettingsStore? store = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "StockViewer-avalonia-tests-" + Guid.NewGuid().ToString("N"));
        var main = new MainWindow(store ?? new SettingsStore(directory), false)
        { WindowStartupLocation = WindowStartupLocation.Manual, Position = new(160, 130) };
        main.Show(); return main;
    }
    private static void SaveImage(Control window, string name)
    {
        Directory.CreateDirectory("artifacts/ui");
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine("artifacts/ui", name), PngBitmapEncoderOptions.Default);
    }
    private static void HideSearch(MainWindow main) => Find<Button>(main, "CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void RightClick(QuoteWindow popup, int count)
    { popup.ProcessMouseDown(MouseButton.Right, count); popup.ProcessMouseUp(MouseButton.Right); }
    private static string Tick(string code, string price, string percent) => JsonSerializer.Serialize(new
    { trnm = "REAL", data = new[] { new { type = "0B", item = code, values = new Dictionary<string, string> { ["10"] = price, ["12"] = percent, ["20"] = "091501" } } } });
    private static void Chord(QuoteWindow popup, MouseButton first = MouseButton.Left, MouseButton firstRelease = MouseButton.Right)
    {
        popup.ProcessMouseDown(first, 2); popup.ProcessMouseDown(first == MouseButton.Left ? MouseButton.Right : MouseButton.Left, 2);
        popup.ProcessMouseUp(firstRelease); popup.ProcessMouseUp(firstRelease == MouseButton.Left ? MouseButton.Right : MouseButton.Left);
    }
    private static async Task UiTests()
    {
        var main = MakeMain(); await Idle();
        var service = new DemoMarketData(); await main.UseServiceAsync(service, DemoMarketData.SampleStocks);
        Check(main.Bounds.Size == new Size(286, 80), "search window matches the WPF 286 x 80 DIP size");
        SaveImage(main, "search-empty.png");
        var buttons = main.GetVisualDescendants().OfType<Button>().Where(x => x.IsEffectivelyVisible && x is not CheckBox).ToArray();
        Check(buttons.Length == 6 && buttons.All(b => b.Name is "SaveLayoutButton" or "LoadLayoutButton" or "ThemeButton" or "CloseButton" or "MinimizeButton" or "SearchButton"), "save, load, theme, minimize, X and search buttons are visible");
        Check(Find<Button>(main, "SaveLayoutButton").Bounds.Width == 22
            && Find<CheckBox>(main, "DisplayName").Bounds.Right <= Find<Button>(main, "SaveLayoutButton").TranslatePoint(default, main)!.Value.X,
            "the compact toolbar leaves room for the full display-name checkbox");
        var display = Find<CheckBox>(main, "DisplayName"); var close = Find<Button>(main, "CloseButton");
        Check(Math.Abs(display.TranslatePoint(new Point(0, display.Bounds.Height / 2), main)!.Value.Y
            - close.TranslatePoint(new Point(0, close.Bounds.Height / 2), main)!.Value.Y) < 2, "name checkbox and X share one horizontal row");
        var searchCard = Find<Border>(main, "SearchCard");
        Check(((SolidColorBrush)searchCard.Background!).Color == Color.Parse("#F7F8FA") && searchCard.CornerRadius == new CornerRadius(8), "canvas color and search corner radius match WPF");
        Check(close.ClickMode == ClickMode.Press, "X still acts on the first press");
        SaveImage(main, "search-empty.png");
        display.IsChecked = false;
        var first = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!; await Idle();
        Check(first.DisplayedPrice.EndsWith("원") && !first.NameVisible, "domestic popup opens and captures the hidden-name preference");
        var hiddenWidth = first.Bounds.Width;
        SaveImage(first, "quote-price-only.png");
        display.IsChecked = true;
        var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; await Idle();
        Check(!first.NameVisible && second.NameVisible && main.Popups.Count == 2, "popup name settings remain independent");
        var replacement = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!; await Idle();
        Check(!first.IsVisible && replacement.NameVisible && main.Popups.Count == 2, "duplicate symbols replace their own popup only");
        SaveImage(replacement, "quote-named.png");
        Check(replacement.Bounds.Height == 32 && replacement.Bounds.Width < 300 && replacement.Bounds.Width > hiddenWidth, "quote matches the compact one-line geometry and shrinks when unnamed");
        Check(replacement.GetVisualDescendants().OfType<TextBlock>().Count(t => t.IsEffectivelyVisible) == 3, "quote has exactly name, price and daily rate");
        Check(replacement.Position.Y >= main.Position.Y + main.Bounds.Height * main.RenderScaling, "new quote leaves search input uncovered");
        SaveImage(replacement, "quote-named.png");
        replacement.SetConnection(new("connected", true)); replacement.ResetQuote();
        var now = DateTimeOffset.UtcNow;
        replacement.ApplyQuote(new("005930", 72500, -1.25m, now, "091500", Source: "네이버 공개 시세", SourceTime: now));
        Check(replacement.DisplayedPrice == "72,500원" && Find<TextBlock>(replacement, "PercentText").Text == "-1.25%", "Korean currency and daily percentage match");
        Check(((SolidColorBrush)Find<TextBlock>(replacement, "PriceText").Foreground!).Color == Color.Parse("#3182F6"), "falling price uses the original blue");
        replacement.ApplyQuote(new("005930", 73000, 1.5m, now.AddSeconds(1), Source: "네이버 공개 시세"));
        Check(replacement.DisplayedPrice == "73,000원" && replacement.StatusMessage.Contains("수신"), "timestamp-free fresh public prices update and show receive time");
        replacement.ApplyQuote(new("005930", 1, 0, now.AddSeconds(2), Source: "네이버 공개 시세", SourceTime: now.AddSeconds(-1)));
        Check(replacement.DisplayedPrice == "73,000원", "older source data cannot overwrite a new public price");
        replacement.SetConnection(new("other failure", Code: "000660"));
        Check(Find<Border>(replacement, "Card").Opacity == 1, "another symbol's error does not dim this popup");
        replacement.SetConnection(new("reconnecting", Code: "005930"));
        Check(Find<Border>(replacement, "Card").Opacity == 1, "failed symbol retains its last price and fully opaque background");
        var us = new QuoteWindow(new("US:AAPL.O", "애플", "NASDAQ", "AAPL"), true);
        us.Show(); us.SetConnection(new("connected", true));
        us.ApplyQuote(new("US:AAPL.O", 1234.50m, 1.25m, now, Currency: "USD")); await Idle();
        Check(us.DisplayedPrice == "$1,234.50" && us.Bounds.Height == 32, "US dollars fit the original one-line price popup");
        SaveImage(us, "quote-us.png");
        us.ApplyQuote(new("US:AAPL.O", .1234m, -1, now.AddSeconds(1), Currency: "USD"));
        Check(us.DisplayedPrice == "$0.1234", "sub-dollar precision is preserved"); us.Close();
        HideSearch(main); await Idle();
        Check(!main.IsVisible && replacement.IsVisible && second.IsVisible, "X hides search and keeps independent quotes alive");
        replacement.Close(); second.Close(); await Idle();
    }
    private static async Task GestureTests()
    {
        foreach (var fixedAtStart in new[] { false, true })
        foreach (var first in new[] { MouseButton.Left, MouseButton.Right })
        foreach (var releaseFirst in new[] { MouseButton.Left, MouseButton.Right })
        {
            var popup = new QuoteWindow(DemoMarketData.SampleStocks[0], true) { Position = new(200, 180) };
            popup.Show(); await Idle(); popup.SetFixed(fixedAtStart);
            var requests = 0; popup.SearchRequested += (_, _) => requests++;
            popup.ProcessMouseDown(first, 2);
            popup.ProcessMouseDown(first == MouseButton.Left ? MouseButton.Right : MouseButton.Left, 2);
            popup.ProcessMouseUp(releaseFirst); await Idle();
            Check(requests == 0 && popup.IsVisible && popup.IsFixed == fixedAtStart, "chord waits for both releases and overrides Fix and double-close");
            popup.ProcessMouseUp(releaseFirst == MouseButton.Left ? MouseButton.Right : MouseButton.Left);
            Check(requests == 0 && popup.IsVisible, "last chord release defers search until input dispatch completes"); await Idle();
            Check(requests == 1 && popup.IsFixed == fixedAtStart, "either press/release order requests search exactly once");
            RightClick(popup, 2); await Idle(); Check(popup.IsVisible && popup.IsFixed != fixedAtStart, "right click after a chord ignores its stale double-click count");
            popup.ProcessMouseDown(MouseButton.Right, 2); Check(popup.IsVisible, "second right down does not destroy quote");
            popup.ProcessMouseUp(MouseButton.Right); Check(popup.IsVisible, "right up is processed before deferred close");
            await Idle(); Check(!popup.IsVisible, "double right closes after release");
        }
        var drag = new QuoteWindow(DemoMarketData.SampleStocks[0], true) { Position = new(240, 220) };
        drag.Show(); await Idle(); var start = drag.Position;
        drag.ProcessMouseDown(MouseButton.Left, screenPosition: start);
        drag.ProcessMouseMove(new(start.X + 30, start.Y + 25), true, false); drag.ProcessMouseUp(MouseButton.Left);
        Check(drag.Position == new PixelPoint(start.X + 30, start.Y + 25), "Free left drag uses physical screen coordinates");
        drag.SetFixed(true); var fixedPosition = drag.Position;
        drag.ProcessMouseDown(MouseButton.Left, screenPosition: fixedPosition); drag.ProcessMouseMove(new(600, 500), true, false); drag.ProcessMouseUp(MouseButton.Left);
        Check(drag.Topmost && drag.Position == fixedPosition, "Fix enables topmost and prevents left dragging");
        drag.Position = new(650, 500); await Idle(); Check(drag.Position == fixedPosition, "Fix also rejects direct native/window movement");
        drag.Close();
        Check(PopupPlacement.BelowAnchor(new(500, 100, 170, 32), new(286, 80), new(0, 0, 1920, 1040)) == new PixelPoint(442, 136), "search is centered below a quote with four-DIP gap");
        Check(PopupPlacement.BelowAnchor(new(500, 1000, 170, 32), new(286, 80), new(0, 0, 1920, 1040)) == new PixelPoint(442, 916), "search flips above a bottom-edge quote");
        Check(PopupPlacement.BelowAnchor(new(-1600, 60, 170, 32), new(286, 80), new(-1600, 0, 1600, 900)).X == -1600, "negative-coordinate monitors clamp within their own work area");
        Check(PopupPlacement.BelowAnchor(new(750, 150, 255, 48), new(429, 120), new(0, 0, 1920, 1040), 1.5) == new PixelPoint(663, 204), "150-percent DPI preserves the original four-DIP gap and center");
    }
    private static async Task SearchTests()
    {
        var main = MakeMain(); var remote = new DelayedSearchService();
        await main.UseServiceAsync(remote, DemoMarketData.SampleStocks); await Idle(); main.Activate();
        var box = Find<TextBox>(main, "SearchBox"); var results = Find<ListBox>(main, "SearchResults");
        box.Focus();
        foreach (var query in new[] { "큐리", "삼성전자", "NAVER", "005930", "SK하이닉스" })
        {
            box.Text = ""; await Idle(); box.Text = query; await Idle();
            Check(box.Text == query && box.IsFocused, "clear and retype remains responsive: " + query);
        }
        var requestCount = remote.Requests.Count; box.Text = "큐리";
        await Until(() => remote.Requests.Count > requestCount);
        remote.Requests[^1].Reply.TrySetResult([new("115180", "큐리언트", "KOSDAQ"), new("445680", "큐리옥스바이오시스템즈", "KOSDAQ")]);
        await Until(() => results.ItemCount == 2);
        Check(results.SelectedIndex == 0, "remote Korean autocomplete selects the first valid result");
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await Until(() => main.Popups.ContainsKey("445680"));
        Check(true, "Enter opens the selected remote Korean stock");
        var popup = main.Popups["445680"];
        var suggestions = Find<Avalonia.Controls.Primitives.Popup>(main, "Suggestions");
        Check(!suggestions.IsOpen, "selection closes suggestions");
        popup.Close(); await Idle(); main.Activate(); box.Focus(); await Idle();
        Check(!suggestions.IsOpen, "focus restoration after quote close never opens autocomplete");
        box.Text = "삼성"; await Idle(); SaveImage(main, "search-typed.png");
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        var oldCount = remote.Requests.Count;
        await Task.Delay(300);
        remote.Requests[^1].Reply.TrySetResult([DemoMarketData.SampleStocks[0]]); await Idle();
        Check(!suggestions.IsOpen, "late remote responses do not reopen a dismissed list");
        box.Text = ""; box.Text = "애플";
        await Until(() => remote.Requests[^1].Query == "애플");
        remote.Requests[^1].Reply.TrySetResult([new("US:AAPL.O", "애플", "NASDAQ", "AAPL")]);
        await Until(() => results.ItemCount == 1);
        box.Text = "aapl";
        await Until(() => remote.Requests[^1].Query == "aapl");
        remote.Requests[^1].Reply.TrySetResult([new("US:AAPL.O", "애플", "NASDAQ", "AAPL")]); await Idle();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await Until(() => main.Popups.ContainsKey("US:AAPL.O"));
        Check(true, "lowercase ticker resolves the same US popup as its Korean name");
        main.Close(); foreach (var item in main.Popups.Values.ToArray()) item.Close(); await Idle();
    }
    private static async Task LifetimeTests()
    {
        var main = MakeMain(); var demo = new DemoMarketData(); await main.UseServiceAsync(demo, DemoMarketData.SampleStocks);
        var popup = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!; await Idle(); popup.Position = new(400, 250);
        var ended = 0; main.SessionEnded += (_, _) => ended++;
        var display = Find<CheckBox>(main, "DisplayName"); display.IsChecked = false;
        var box = Find<TextBox>(main, "SearchBox"); box.Text = "삼성";
        HideSearch(main); await Idle(); Check(!main.IsVisible && popup.IsVisible && ended == 0, "X only hides search with prices open");
        Chord(popup); await Idle();
        Check(main.IsVisible && main.Position.Y >= popup.Position.Y + popup.Bounds.Height * popup.RenderScaling, "chord recalls hidden search below the quote");
        Check(box.Text == "삼성" && display.IsChecked == false && !Find<Avalonia.Controls.Primitives.Popup>(main, "Suggestions").IsOpen, "recall preserves text and preference without opening autocomplete");
        main.WindowState = WindowState.Minimized; await Idle(); Chord(popup); await Idle();
        Check(main.IsVisible && main.WindowState == WindowState.Normal, "chord also restores a minimized search window");
        if (native)
        {
            var screen = popup.Screens.ScreenFromWindow(popup)!;
            popup.Position = new(popup.Position.X, screen.WorkingArea.Bottom - (int)(popup.Bounds.Height * popup.RenderScaling));
            Chord(popup); await Idle();
            Check(main.Position.Y + main.Bounds.Height * main.RenderScaling <= popup.Position.Y, "native recall uses space above a quote at the bottom of its monitor");
        }
        var previous = popup.LastQuote;
        HideSearch(main); await Until(() => !ReferenceEquals(previous, popup.LastQuote));
        Check(!main.IsVisible && popup.IsVisible, "price updates continue while search is hidden");
        popup.Close(); await Until(() => ended == 1); Check(true, "last quote ends a hidden search session exactly once");
        var pending = MakeMain(); var slow = new DelayedQuoteService();
        await pending.UseServiceAsync(slow, DemoMarketData.SampleStocks);
        var opening = pending.OpenStockAsync(DemoMarketData.SampleStocks[0]);
        await Until(() => slow.SnapshotRequested);
        Check(pending.IsEnabled && Find<TextBox>(pending, "SearchBox").IsEnabled, "initial network wait never disables search input");
        pending.Close();
        Check(await opening is null && pending.Popups.Count == 0, "native close cancels an initial quote before creating any price window");
        slow.Snapshot.TrySetResult(new("005930", 73000, 1.5m, DateTimeOffset.UtcNow)); await Idle();
        Check(slow.Disposed && pending.DataDelayNotice is null && pending.Popups.Count == 0,
            "a late response after application close creates neither a quote nor a delay notice");
    }
}
