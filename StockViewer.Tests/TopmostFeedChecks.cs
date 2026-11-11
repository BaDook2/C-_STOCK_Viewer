using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private sealed class FlappingQuoteService : IMarketDataService
    {
        public bool IsDemo => false;
        public event Action<Quote>? QuoteReceived;
        public event Action<ConnectionStatus>? StatusChanged;
        public void Publish(Quote quote) => QuoteReceived?.Invoke(quote);
        public void Connection(ConnectionStatus status) => StatusChanged?.Invoke(status);
        public void Start() => Connection(new("connected", true));
        public Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
            => Task.FromResult<IReadOnlyList<Stock>>(DemoMarketData.SampleStocks);
        public Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
            => Task.FromResult(new Quote(code, 10000, 1, DateTimeOffset.UtcNow,
                Source: "토스증권 공개 시세 · KRX+NXT 통합", SourceTime: DateTimeOffset.UtcNow, MarketStatus: "OPEN"));
        public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task TopmostFeedFlapTests()
    {
        var feed = new FlappingQuoteService();
        var stocks = DemoMarketData.SampleStocks.Take(20).ToArray();
        var main = MakeMain(); await main.UseServiceAsync(feed, stocks);
        var probe = new Window { Title = "StockViewer topmost test", Width = 200, Height = 100,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new(180, 190), ShowInTaskbar = false };
        var quotes = new List<QuoteWindow>();
        var nativeChecks = 0; var nativeLosses = new List<string>(); var phase = "initial"; var freeAudit = false;
        void Observe()
        {
            if (!native || !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())) return;
            foreach (var quote in quotes.Where(q => q.IsVisible && (q.IsFixed || freeAudit)))
            {
                var pinned = PopupTopmost.Read(quote); nativeChecks++;
                if (pinned != quote.IsFixed && nativeLosses.Count < 20) nativeLosses.Add(phase + ": " + quote.Stock.Code + " desired=" + quote.IsFixed + " native=" + pinned);
            }
        }
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        { if (e.Property == Window.TopmostProperty || e.Property == Window.ClientSizeProperty) Observe(); }
        var monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        monitor.Tick += (_, _) => Observe();
        try
        {
            foreach (var stock in stocks)
            {
                var quote = (await main.OpenStockAsync(stock))!; quotes.Add(quote); RightClick(quote, 1);
                quote.PropertyChanged += Changed;
            }
            await Idle();
            var handles = quotes.Select(q => q.TryGetPlatformHandle()?.Handle).ToArray();
            var positions = quotes.Select(q => q.Position).ToArray();
            probe.Show(); probe.Activate(); await Idle(); Observe(); monitor.Start();
            for (var cycle = 0; cycle < 100; cycle++)
            {
                if (cycle == 50) { quotes[^1].SetFixed(false); freeAudit = true; }
                if (cycle % 25 == 0) Application.Current!.RequestedThemeVariant = cycle % 50 == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
                phase = "missing " + cycle;
                foreach (var quote in quotes) feed.Connection(new("가격 데이터 미수신 · 마지막 가격 유지", Code: quote.Stock.Code));
                await Task.Delay(30); Observe();
                Check(quotes.All(q => q.LastQuote is not null && q.StatusMessage.Contains("미수신") && q.IsVisible),
                    "all twenty prices retain their last value while the feed is missing");
                phase = "received " + cycle;
                foreach (var (quote, i) in quotes.Select((q, i) => (q, i)))
                {
                    var price = cycle % 2 == 0 ? 999m + i : 99999999m + i;
                    var now = DateTimeOffset.UtcNow;
                    feed.Publish(new(quote.Stock.Code, price, cycle % 2 == 0 ? -1.25m : 2.5m, now,
                        Source: "토스증권 공개 시세 · KRX+NXT 통합", SourceTime: now,
                        MarketStatus: cycle % 3 == 0 ? "CLOSE" : "OPEN"));
                    feed.Connection(new("connected", true, Code: quote.Stock.Code));
                }
                await Task.Delay(30); probe.Activate(); Observe();
                Check(quotes.Select((q, i) => q.IsVisible && q.IsFixed == (cycle < 50 || i < 19)
                    && q.Topmost == q.IsFixed && (!q.IsFixed || q.Position == positions[i])
                    && q.TryGetPlatformHandle()?.Handle == handles[i] && !q.StatusMessage.Contains("미수신")).All(x => x),
                    "received prices preserve pin state, fixed positions and original native windows after a missing phase");
            }
            await Idle(); Observe();
            Check(nativeLosses.Count == 0, "native topmost never drops during one hundred missing/received cycles: " + string.Join("; ", nativeLosses));
            if (native && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            {
                Check(quotes.Take(19).All(q => IsAboveNormalProbe(q, probe)), "all nineteen pinned prices remain above an activated normal application window");
                Check(PopupTopmost.Read(quotes[^1]) == false, "feed recovery never re-pins the one window deliberately unpinned by the user");
                Console.WriteLine($"Topmost flap stress: 20 windows; 100 cycles; 4,000 connection transitions; {nativeChecks} native observations; {nativeLosses.Count} drops.");
                monitor.Stop();
                var free = quotes[^1]; var handle = free.TryGetPlatformHandle()!;
                if (OperatingSystem.IsWindows()) SetWindowPos(handle.Handle, new(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                else ObjcSetLevel(handle.HandleDescriptor == "NSView" ? ObjcSend(handle.Handle, ObjcSelector("window")) : handle.Handle,
                    ObjcSelector("setLevel:"), 3);
                await Until(() => PopupTopmost.Read(free) == false);
                Check(quotes.Take(19).All(q => PopupTopmost.Read(q) == true),
                    "an unwanted native promotion of a Free window is blocked or repaired without demoting the nineteen pinned prices");
            }
        }
        finally
        {
            monitor.Stop(); foreach (var quote in quotes) quote.PropertyChanged -= Changed;
            probe.Close(); main.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
        await HttpRecoveryTopmostTests();
    }

    private static bool IsAboveNormalProbe(Window pinned, Window probe)
    {
        if (OperatingSystem.IsWindows())
        {
            var target = pinned.TryGetPlatformHandle()!.Handle;
            for (var handle = NativePreviousWindow(probe.TryGetPlatformHandle()!.Handle, 3); handle != IntPtr.Zero;
                handle = NativePreviousWindow(handle, 3)) if (handle == target) return true;
            return false;
        }
        IntPtr MacWindow(Window window)
        {
            var handle = window.TryGetPlatformHandle()!;
            return handle.HandleDescriptor == "NSView" ? ObjcSend(handle.Handle, ObjcSelector("window")) : handle.Handle;
        }
        var a = ObjcSend(MacWindow(pinned), ObjcSelector("orderedIndex")).ToInt64();
        var b = ObjcSend(MacWindow(probe), ObjcSelector("orderedIndex")).ToInt64();
        return a > 0 && b > 0 && a < b;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern IntPtr NativePreviousWindow(IntPtr window, uint command);

    private static async Task HttpRecoveryTopmostTests()
    {
        var clock = new DayClock { Now = DateTimeOffset.Parse("2026-11-11T10:00:00+09:00") };
        var handler = new DomesticHttpFixture(clock);
        await using var service = new PublicMarketData(new PublicHttpFixture(), domesticHandler: handler, domesticClock: clock);
        var stocks = DemoMarketData.SampleStocks.Take(20).ToArray(); var main = MakeMain();
        QuoteWindow[] quotes = []; var nativeDrops = 0; var observations = 0;
        var monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        monitor.Tick += (_, _) =>
        {
            if (!native) return;
            foreach (var quote in quotes.Where(q => q.IsVisible && q.IsFixed))
            { observations++; if (PopupTopmost.Read(quote) != true) nativeDrops++; }
        };
        try
        {
            await main.UseServiceAsync(service, stocks);
            quotes = (await Task.WhenAll(stocks.Select(main.OpenStockAsync))).Select(q => q!).ToArray();
            foreach (var quote in quotes) quote.SetFixed(true);
            HideSearch(main); await Idle(); monitor.Start();
            var handles = quotes.Select(q => q.TryGetPlatformHandle()?.Handle).ToArray();
            for (var cycle = 0; cycle < 5; cycle++)
            {
                handler.FailPrices = true;
                await Until(() => quotes.All(q => q.StatusMessage.Contains("조회 지연")));
                Check(quotes.All(q => q.IsFixed && q.Topmost && (!native || PopupTopmost.Read(q) == true)),
                    "Toss HTTP failures keep all twenty native price windows pinned");
                handler.FailPrices = false; handler.Price += 100; clock.Now = clock.Now.AddSeconds(2);
                await Until(() => quotes.All(q => q.LastQuote?.Price == handler.Price && !q.StatusMessage.Contains("조회 지연")));
                Check(quotes.Select((q, i) => q.IsFixed && q.Topmost && q.TryGetPlatformHandle()?.Handle == handles[i]
                    && (!native || PopupTopmost.Read(q) == true)).All(x => x),
                    "Toss HTTP recovery updates the original twenty pinned windows without replacing or demoting them");
            }
            foreach (var missing in new[] { quotes[0], quotes[^1] })
            {
                handler.MissingCode = missing.Stock.Code;
                await Until(() => missing.StatusMessage.Contains("조회 지연")
                    && quotes.Where(q => q != missing).All(q => !q.StatusMessage.Contains("조회 지연")));
                Check(quotes.All(q => q.IsFixed && q.Topmost && (!native || PopupTopmost.Read(q) == true)),
                    "a single missing Toss symbol cannot unpin that window or any of the healthy nineteen");
                handler.MissingCode = null; handler.Price += 100; clock.Now = clock.Now.AddSeconds(2);
                await Until(() => missing.LastQuote?.Price == handler.Price && !missing.StatusMessage.Contains("조회 지연"));
                Check(quotes.Select((q, i) => q.IsFixed && q.Topmost && q.TryGetPlatformHandle()?.Handle == handles[i]
                    && (!native || PopupTopmost.Read(q) == true)).All(x => x),
                    "a previously missing symbol recovers in the same pinned native window");
            }
            Check(nativeDrops == 0, "native topmost never drops while HTTP failures and recoveries alternate with search hidden");
            Console.WriteLine($"Topmost HTTP stress: 20 windows; 5 HTTP failure/recovery cycles; 2 individual missing-symbol recoveries; {observations} native observations; {nativeDrops} drops; no native-handle loss.");
        }
        finally { monitor.Stop(); main.Close(); await Idle(); }
    }
}
