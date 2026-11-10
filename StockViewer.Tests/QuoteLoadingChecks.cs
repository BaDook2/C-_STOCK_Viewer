using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private sealed class ControlledQuotes : IMarketDataService
    {
        public List<(string Code, CancellationToken Token, TaskCompletionSource<Quote> Reply)> Requests { get; } = [];
        public string[] Subscriptions { get; private set; } = [];
        public bool IsDemo => true;
        public bool Disposed { get; private set; }
        public event Action<Quote>? QuoteReceived;
        public event Action<ConnectionStatus>? StatusChanged;
        public void Start() => StatusChanged?.Invoke(new("connected", true, true));
        public void Publish(Quote quote) => QuoteReceived?.Invoke(quote);
        public Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
            => Task.FromResult<IReadOnlyList<Stock>>(DemoMarketData.SampleStocks);
        public Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
        {
            var reply = new TaskCompletionSource<Quote>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((code, cancellation, reply)); return reply.Task; // Deliberately ignores cancellation to exercise late replies.
        }
        public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default)
        { Subscriptions = codes.ToArray(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private static async Task LoadingPopupTests()
    {
        var apple = new Stock("US:AAPL.O", "애플", "NASDAQ", "AAPL");
        var main = MakeMain(); var slow = new ControlledQuotes();
        await main.UseServiceAsync(slow, [apple]); await Idle();
        try
        {
            var opening = main.OpenStockAsync(apple); var duplicate = main.OpenStockAsync(apple);
            await Until(() => slow.Requests.Count == 1); await Idle();
            Check(ReferenceEquals(opening, duplicate) && slow.Requests.Count == 1, "repeated searches for a pending symbol share one initial request");
            Check(main.Popups.Count == 0 && main.PendingQuoteCount == 1 && main.DataDelayNotice is null,
                "no price window or dash placeholder is created before the first valid price");
            Check(main.IsEnabled && Find<TextBox>(main, "SearchBox").IsEnabled && slow.Subscriptions.Contains(apple.Code),
                "pending first quotes keep search usable and receive live subscriptions");
            slow.Publish(new(apple.Code, 0, 0, DateTimeOffset.UtcNow)); await Idle();
            Check(!opening.IsCompleted && main.Popups.Count == 0, "an invalid first quote never opens a zero or placeholder price");
            slow.Requests[0].Reply.TrySetResult(new(apple.Code, 350.88m, 2.48m, DateTimeOffset.UtcNow, Currency: "USD"));
            var popup = (await opening)!; await Idle();
            Check(popup.IsVisible && popup.LastQuote is not null && popup.DisplayedPrice == "$350.88"
                && popup.ClientSize.Width > 0 && popup.ClientSize.Height == 32,
                "the first visible native price window is already populated with the received Apple quote");
            Check(Math.Abs(popup.Position.X + popup.ClientSize.Width * popup.RenderScaling / 2
                - main.Position.X - main.ClientSize.Width * main.RenderScaling / 2) <= popup.RenderScaling,
                "a quote-first popup remains centered beneath search");
            var original = popup;
            var replacement = main.OpenStockAsync(apple); await Until(() => slow.Requests.Count == 2);
            Check(original.IsVisible && ReferenceEquals(main.Popups[apple.Code], original), "reopening a symbol preserves the old valid popup until its replacement has data");
            slow.Publish(new(apple.Code, 351, 2.5m, DateTimeOffset.UtcNow, Currency: "USD"));
            var updated = (await replacement)!; await Idle();
            Check(!original.IsVisible && updated.DisplayedPrice == "$351.00" && updated.IsVisible,
                "a live subscription can supply the first price even while its snapshot is still pending");
            slow.Requests[1].Reply.TrySetResult(new(apple.Code, 1, 1, DateTimeOffset.UtcNow, Currency: "USD")); await Idle();
            Check(ReferenceEquals(main.Popups[apple.Code], updated) && updated.DisplayedPrice == "$351.00",
                "a late initial snapshot cannot overwrite or replace the already opened quote");
        }
        finally { main.Close(); foreach (var request in slow.Requests) request.Reply.TrySetCanceled(); }
    }
    private static async Task LoadingDeadlineTests()
    {
        var main = MakeMain(); var slow = new ControlledQuotes();
        await main.UseServiceAsync(slow, DemoMarketData.SampleStocks); await Idle();
        try
        {
            var watch = Stopwatch.StartNew();
            var first = main.OpenStockAsync(DemoMarketData.SampleStocks[0]);
            var second = main.OpenStockAsync(DemoMarketData.SampleStocks[1]);
            await Until(() => slow.Requests.Count == 2); await Task.Delay(150);
            Check(main.DataDelayNotice is null && main.Popups.Count == 0, "the delay notice does not appear before the three-second deadline");
            slow.Requests[0].Reply.TrySetException(new IOException("network fixture"));
            var results = await Task.WhenAll(first, second); await Idle();
            Check(watch.Elapsed >= TimeSpan.FromSeconds(3) && watch.Elapsed < TimeSpan.FromSeconds(6)
                && results.All(x => x is null) && main.PendingQuoteCount == 0,
                "no data, including a failed snapshot, produces a notice after three seconds and clears pending opens");
            var notice = main.DataDelayNotice!;
            Check(notice.IsVisible && Find<TextBlock>(notice, "StockNames").Text!.Contains("삼성전자")
                && Find<TextBlock>(notice, "StockNames").Text!.Contains("SK하이닉스"),
                "concurrent timeouts share one notice identifying both stocks");
            Check(notice.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("3초 이상") == true),
                "the visible notice explicitly says no price data has arrived for at least three seconds");
            Check(main.Popups.Count == 0 && slow.Subscriptions.Length == 0 && slow.Requests.All(r => r.Token.IsCancellationRequested),
                "timed-out opens cancel subscriptions without creating blank price windows");
            SaveImage(notice, "quote-data-delay.png");
            slow.Requests[1].Reply.TrySetResult(new("000660", 1, 0, DateTimeOffset.UtcNow));
            slow.Publish(new("005930", 73000, 1, DateTimeOffset.UtcNow)); await Idle();
            Check(main.Popups.Count == 0, "late data after the timeout cannot create an unexpected price popup");
            Find<Button>(notice, "DismissButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(main.DataDelayNotice is null && main.IsEnabled, "the timeout notice closes and search remains available");
            var retry = main.OpenStockAsync(DemoMarketData.SampleStocks[0]); await Until(() => slow.Requests.Count == 3);
            slow.Requests[2].Reply.TrySetResult(new("005930", 73000, 1, DateTimeOffset.UtcNow));
            Check((await retry)?.DisplayedPrice == "73,000원", "searching again after timeout creates a new successful initial request");
            var failedReplacement = main.OpenStockAsync(DemoMarketData.SampleStocks[0]); await Until(() => slow.Requests.Count == 4);
            Check(main.Popups["005930"].DisplayedPrice == "73,000원", "a replacement keeps the existing displayed price during its initial wait");
            await failedReplacement; await Idle();
            Check(main.Popups["005930"].IsVisible && main.Popups["005930"].DisplayedPrice == "73,000원" && main.DataDelayNotice?.IsVisible == true,
                "a replacement timeout leaves the existing valid price popup open");
        }
        finally { main.Close(); foreach (var request in slow.Requests) request.Reply.TrySetCanceled(); }
    }
    private static async Task NativeCloseTests()
    {
        var main = MakeMain(); var service = new ControlledQuotes();
        await main.UseServiceAsync(service, DemoMarketData.SampleStocks); var ended = 0;
        main.SessionEnded += (_, _) => ended++;
        var firstTask = main.OpenStockAsync(DemoMarketData.SampleStocks[0]);
        await Until(() => service.Requests.Count == 1);
        service.Requests[0].Reply.TrySetResult(new("005930", 73000, 1, DateTimeOffset.UtcNow));
        var first = (await firstTask)!; first.SetFixed(true);
        var secondTask = main.OpenStockAsync(DemoMarketData.SampleStocks[1]);
        await Until(() => service.Requests.Count == 2);
        service.Requests[1].Reply.TrySetResult(new("000660", 100000, -1, DateTimeOffset.UtcNow));
        var second = (await secondTask)!;
        var pending = main.OpenStockAsync(DemoMarketData.SampleStocks[2]);
        await Until(() => service.Requests.Count == 3);
        main.WindowState = WindowState.Minimized; await Idle(); main.Close();
        Check(await pending is null, "native close cancels pending prices even when the taskbar window is minimized");
        await Until(() => ended == 1); await Idle();
        Check(!main.IsVisible && !first.IsVisible && !second.IsVisible && service.Disposed && main.Popups.Count == 0,
            "native application close shuts down all fixed and free price windows and the market service");
        Check(main.DataDelayNotice is null && ended == 1, "application close creates no timeout notice and signals session end once");
        foreach (var request in service.Requests) request.Reply.TrySetCanceled();

        var switched = MakeMain(); var old = new ControlledQuotes();
        await switched.UseServiceAsync(old, DemoMarketData.SampleStocks);
        var opening = switched.OpenStockAsync(DemoMarketData.SampleStocks[0]); await Until(() => old.Requests.Count == 1);
        await switched.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        Check(await opening is null && switched.DataDelayNotice is null && switched.Popups.Count == 0,
            "a replaced service cancels its pending open without showing stale data or a delay warning");
        old.Requests[0].Reply.TrySetResult(new("005930", 1, 1, DateTimeOffset.UtcNow)); await Idle();
        Check(switched.Popups.Count == 0, "a late response from a replaced service cannot open a popup"); switched.Close();
    }
}
