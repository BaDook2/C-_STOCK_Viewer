using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StockViewer.App;
using StockViewer.Core;

namespace StockViewer.Tests;

public static class Program
{
    private static int passed;
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/StockViewer;component/Styles/TossInspired.xaml", UriKind.Relative) });
        var result = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                if (args.Contains("--public-smoke")) await LivePublicSmoke();
                else { await CoreTests(); await TransportTests(); await PublicDataTests(); await UiTests(); }
                Console.WriteLine($"PASS: {passed} assertions; no live brokerage calls.");
            }
            catch (Exception e) { Console.Error.WriteLine(e); result = 1; }
            finally { app.Shutdown(result); }
        };
        app.Run(); return result;
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
    private static async Task Throws<T>(Func<Task> action, string label) where T : Exception
    { try { await action(); } catch (T) { Check(true, label); return; } throw new Exception("FAIL: " + label); }
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static Task CoreTests()
    {
        var stocks = DemoMarketData.SampleStocks;
        Check(StockSearch.Find(stocks, "삼성전자")[0].Code == "005930", "exact name ranks first");
        Check(StockSearch.Find(stocks, "005930").Single().Name == "삼성전자", "code search");
        Check(StockSearch.Find(stocks, "ㅅㅅㅈㅈ").Any(s => s.Code == "005930"), "Hangul initial search");
        Check(StockSearch.Find(stocks, "  삼 성 전 자  ")[0].Code == "005930", "whitespace normalization");
        Check(StockSearch.Find(stocks, "naver").Single().Code == "035420", "Latin case insensitive search");
        Check(StockSearch.Find(stocks, "zzzz").Length == 0, "no matches");
        Check(StockSearch.Find(stocks, " ").Length == 0, "blank query");
        Check(StockSearch.Find(stocks, "삼성", 2).Length == 2, "bounded results");
        Check(StockCode.IsValid("0000A0") && !StockCode.IsValid("123/45"), "alphanumeric domestic instrument codes");
        var quote = KiwoomParser.Snapshot("005930", Json("{\"cur_prc\":\"-72,500\",\"flu_rt\":\"-1.25\"}"));
        Check(quote.Price == 72500 && quote.ChangePercent == -1.25m, "negative price sign normalized; negative percent retained");
        var tick = KiwoomParser.Realtime(Json(Tick("005930", "+73500", "+2.50"))).Single();
        Check(tick.Price == 73500 && tick.ChangePercent == 2.50m && tick.TradeTime == "091501", "0B trade fields");
        Check(!KiwoomParser.Realtime(Json("{\"trnm\":\"PING\"}")).Any(), "ignore non-trade messages");
        Check(!KiwoomParser.Realtime(Json(Tick("005930", "bad", "+2.50"))).Any(), "ignore malformed trade row");
        Check(new ApiCredentials("sensitive-app", "sensitive-secret").ToString().Contains("hidden"), "credentials ToString redacted");
        return Task.CompletedTask;
    }
    private static async Task TransportTests()
    {
        var handler = new FakeHttp();
        var socket = new FakeSocket();
        await using var client = new KiwoomClient(new("fixture", "fixture-secret"), handler, () => socket, TimeSpan.Zero);
        var catalog = await client.GetStocksAsync();
        Check(catalog.Any(s => s.Code == "005930") && catalog.Any(s => s.Code == "0000A0"), "catalog parsing and instrument codes");
        Check(handler.Markets.SetEquals(["0", "10", "8", "50"]), "KOSPI/KOSDAQ/ETF/KONEX catalog requests");
        Check(handler.ContinuationRequests == 1, "catalog continuation header");
        var quote = await client.GetSnapshotAsync("005930");
        Check(quote.Price == 72500 && quote.ChangePercent == -1.25m, "REST quote parsing");
        Check(handler.AuthCalls == 1, "token reused across requests");
        Check(handler.ValidHeaders && handler.OnlyReadEndpoints, "authenticated headers and read-only endpoints");
        await Throws<ArgumentException>(async () => await client.GetSnapshotAsync("bad"), "invalid instrument blocked");
        await Throws<InvalidOperationException>(async () => await client.SetSubscriptionsAsync(Enumerable.Range(0, 201).Select(i => i.ToString("D6"))), "subscription limit enforced");
        await client.SetSubscriptionsAsync(["005930", "005930", "000660"]);
        var received = new TaskCompletionSource<Quote>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.QuoteReceived += q => received.TrySetResult(q);
        client.Start();
        await Until(() => socket.Sent.Any(s => s.Contains("LOGIN")));
        Check(!socket.Sent.Any(s => s.Contains("REG")), "REG waits for LOGIN acknowledgement");
        socket.Push("{\"trnm\":\"PING\"}");
        await Until(() => socket.Sent.Any(s => s.Contains("PING")));
        Check(true, "heartbeat echoed during LOGIN");
        socket.Push("{\"trnm\":\"LOGIN\",\"return_code\":0}");
        await Until(() => socket.Sent.Any(s => s.Contains("REG")));
        var registration = Json(socket.Sent.First(s => s.Contains("REG")));
        Check(registration.GetProperty("data")[0].GetProperty("item").GetArrayLength() == 2, "one shared deduplicated registration");
        Check(socket.Endpoint?.AbsolutePath == "/api/dostk/websocket" && socket.Endpoint.Host == "api.kiwoom.com", "official domestic WebSocket endpoint");
        socket.Push(Tick("005930", "+73000", "+1.75"));
        Check((await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).Price == 73000, "WebSocket updates delivered");
        await client.SetSubscriptionsAsync([]);
        Check(socket.Sent.Any(s => s.Contains("REMOVE")), "last popup closes: unsubscribe");
        var bad = new FakeHttp { Reject = true };
        await using var rejected = new KiwoomClient(new("fixture", "fixture-secret"), bad, requestInterval: TimeSpan.Zero);
        await Throws<InvalidOperationException>(() => rejected.AuthenticateAsync(), "authentication rejection handled");
        var repeated = new FakeHttp { RepeatContinuation = true };
        await using var repeatedClient = new KiwoomClient(new("fixture", "fixture-secret"), repeated, requestInterval: TimeSpan.Zero);
        await Throws<InvalidDataException>(async () => await repeatedClient.GetStocksAsync(), "repeated continuation key rejected");
        await using var mock = new KiwoomClient(new("fixture", "fixture-secret", true), new FakeHttp(), requestInterval: TimeSpan.Zero);
        await mock.AuthenticateAsync();
        Check(true, "mock credentials accepted by isolated fixture");
        var firstSocket = new FakeSocket(); var secondSocket = new FakeSocket();
        var sockets = new Queue<FakeSocket>([firstSocket, secondSocket]);
        await using var reconnecting = new KiwoomClient(new("fixture", "fixture-secret"), new FakeHttp(), () => sockets.Dequeue(), TimeSpan.Zero);
        var disconnected = false;
        reconnecting.StatusChanged += s => { if (s.Message.Contains("연결 끊김")) disconnected = true; };
        await reconnecting.SetSubscriptionsAsync(["005930"]); reconnecting.Start();
        await Until(() => firstSocket.Sent.Any(s => s.Contains("LOGIN")));
        firstSocket.Push("{\"trnm\":\"LOGIN\",\"return_code\":0}");
        await Until(() => firstSocket.Sent.Any(s => s.Contains("REG")));
        firstSocket.Push("broken-json");
        await Until(() => secondSocket.Sent.Any(s => s.Contains("LOGIN")));
        Check(disconnected, "broken connection reports stale data and reconnects");
        Check(!secondSocket.Sent.Any(s => s.Contains("REG")), "reconnect also waits for authentication");
        secondSocket.Push("{\"trnm\":\"LOGIN\",\"return_code\":0}");
        await Until(() => secondSocket.Sent.Any(s => s.Contains("REG")));
        Check(Json(secondSocket.Sent.First(s => s.Contains("REG"))).GetProperty("data")[0].GetProperty("item")[0].GetString() == "005930", "subscription restored after reconnect");
    }
    private static async Task PublicDataTests()
    {
        var handler = new PublicHttpFixture();
        await using var service = new PublicMarketData(handler);
        var catalog = await service.GetStocksAsync();
        Check(catalog.Count == 2 && catalog.Any(s => s.Market == "KOSDAQ"), "public catalog without key");
        var results = await service.SearchAsync("삼성");
        Check(results.Count == 2 && results.Any(s => s.Code == "0000A0") && results.All(s => s.Code != "AAPL"), "public autocomplete supports domestic alphanumeric codes");
        var first = await service.GetSnapshotAsync("005930");
        var repeated = await service.GetSnapshotAsync("005930");
        Check(first.Price == 72500 && first.ChangePercent == -1.25m, "public price and direction parse correctly");
        Check(first.SourceTime.HasValue && first.MarketStatus == "CLOSE" && first.Source.Contains("네이버"), "public source timestamp and closed market retained");
        Check(handler.QuoteRequests == 1 && repeated == first, "initial lookup and repeated popup share cached quote");
        Check(!handler.HadAuthorization, "public requests send no authorization or broker key");
        Check(PublicMarketData.ReadPollingInterval(Json("{\"pollingInterval\":1000}")) == TimeSpan.FromSeconds(2), "polling has two-second minimum");
        Check(PublicMarketData.ReadPollingInterval(Json("{\"pollingInterval\":70000}")) == TimeSpan.FromSeconds(70), "provider longer interval is honored");
        Check(PublicMarketData.ParseQuotes(Json("{\"datas\":[{},null,{\"itemCode\":\"005930\",\"closePrice\":\"0\",\"fluctuationsRatio\":\"1\"}]}")).Length == 0,
            "invalid public prices never appear as zero");
        var pollingHandler = new PublicHttpFixture();
        await using var polling = new PublicMarketData(pollingHandler);
        var delivered = new HashSet<string>();
        polling.QuoteReceived += q => delivered.Add(q.Code);
        await polling.SetSubscriptionsAsync(["005930", "000660", "005930"]); polling.Start();
        await Until(() => delivered.Count == 2);
        Check(pollingHandler.QuoteRequests == 1 && pollingHandler.LastBatchSize == 2, "all popups share one batched public request");
        await Until(() => pollingHandler.QuoteRequests >= 2);
        Check(pollingHandler.RequestTimes[1] - pollingHandler.RequestTimes[0] >= TimeSpan.FromSeconds(1.9), "public quotes refresh at the configured two-second interval");
        await polling.SetSubscriptionsAsync([]);
        var count = pollingHandler.QuoteRequests; await Task.Delay(100);
        Check(pollingHandler.QuoteRequests == count, "last popup stops public polling");
        var slowHandler = new PublicHttpFixture { Interval = 70000 };
        await using var slow = new PublicMarketData(slowHandler);
        await slow.GetSnapshotAsync("005930"); await slow.GetSnapshotAsync("005930");
        Check(slow.PollingInterval.TotalSeconds == 70 && slowHandler.QuoteRequests == 1, "closed-market provider interval shared by snapshots");
        var limitedHandler = new PublicHttpFixture { RateLimited = true };
        await using var limited = new PublicMarketData(limitedHandler);
        await Throws<IOException>(async () => await limited.GetSnapshotAsync("005930"), "public rate limit reported");
        await Throws<IOException>(async () => await limited.GetSnapshotAsync("005930"), "rate limit cooldown applies to repeated searches");
        Check(limitedHandler.QuoteRequests == 1, "Retry-After prevents extra network calls");
    }
    private static async Task LivePublicSmoke()
    {
        await using var service = new PublicMarketData();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var stocks = await service.GetStocksAsync(limit.Token);
        Check(stocks.Count > 0 && stocks.Any(s => s.Code == "005930"), "live public catalog returns domestic stocks");
        var results = await service.SearchAsync("삼성", limit.Token);
        Check(results.Any(s => s.Code == "005930"), "live public autocomplete works without a key");
        var quote = await service.GetSnapshotAsync("005930", limit.Token);
        Check(quote.Price > 0 && quote.SourceTime.HasValue, "live public quote has price and provider timestamp");
        Check(service.PollingInterval >= TimeSpan.FromSeconds(2), "live provider polling interval respected");
        Console.WriteLine($"Public smoke: provider interval {service.PollingInterval.TotalSeconds:0.#}s; market {quote.MarketStatus}; no credentials used.");
    }
    private static async Task UiTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StockViewer-tests-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory);
        var credentials = new ApiCredentials("fixture-app-key", "fixture-secret-key");
        store.SaveCredentials(credentials);
        Check(store.LoadCredentials() == credentials, "Windows DPAPI round trip");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "kiwoom.protected"))).Contains("fixture"), "keys encrypted on disk");
        store.DeleteCredentials(); Check(store.LoadCredentials() is null, "saved credentials removal");
        store.SaveDisplayName(false); Check(!store.LoadDisplayName(), "display preference persisted");
        store.SaveCatalog(DemoMarketData.SampleStocks); Check(store.LoadCatalog().Count == DemoMarketData.SampleStocks.Length, "catalog cache round trip");
        var main = new MainWindow(store, false) { Left = 160, Top = 130, WindowStartupLocation = WindowStartupLocation.Manual };
        main.Show(); await Idle();
        Check(main.StatusMessage.Contains("API 키 불필요"), "app starts without broker credentials");
        Check(main.ActualWidth <= 286 && main.ActualHeight <= 80, "compact search window dimensions");
        Check(main.FindName("StatusText") is null && main.FindName("PopupCount") is null && main.FindName("DemoButton") is null,
            "search window has no status bar, counter or demo control");
        Check(VisualChildren<Button>(main).Count(b => b.IsVisible) == 4, "only settings, minimize, close and search buttons");
        var display = (CheckBox)main.FindName("DisplayName");
        var close = (Button)main.FindName("CloseButton");
        Check(Math.Abs(display.TranslatePoint(new Point(0, display.ActualHeight / 2), main).Y
            - close.TranslatePoint(new Point(0, close.ActualHeight / 2), main).Y) < 2, "name checkbox shares the close button row");
        SaveImage(main, "compact-search.png");
        var demo = new DemoMarketData(); await main.UseServiceAsync(demo, await demo.GetStocksAsync()); await Idle();
        var samsung = DemoMarketData.SampleStocks[0];
        var first = (await main.OpenStockAsync(samsung))!; await Idle();
        Check(main.Popups.Count == 1 && first.DisplayedPrice.Contains("원"), "open quote popup");
        Check(first.LastQuote?.IsDemo == true && first.StatusMessage.Contains("DEMO"), "sample prices identified in tooltip");
        Check(!first.NameVisible, "unchecked preference hides name and code");
        Check(Math.Abs(first.Left + first.ActualWidth / 2 - (main.Left + main.ActualWidth / 2)) < 2
            && Math.Abs(first.Top + first.ActualHeight / 2 - (main.Top + main.ActualHeight / 2)) < 2, "popup centered on search window");
        display.IsChecked = true; display.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(!first.NameVisible, "changing checkbox does not alter an existing hidden-name popup");
        var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; await Idle();
        Check(main.Popups.Count == 2 && first.IsVisible && second.IsVisible, "multiple independent symbols");
        Check(second.NameVisible && !first.NameVisible, "each popup captures its own name display preference");
        display.IsChecked = false; display.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(second.NameVisible && !first.NameVisible, "checkbox changes affect future popups only");
        var replacement = (await main.OpenStockAsync(samsung))!; await Idle();
        Check(main.Popups.Count == 2 && !first.IsVisible && !ReferenceEquals(first, replacement), "duplicate replaces only matching popup");
        Check(!replacement.NameVisible && second.NameVisible, "replacement uses current checkbox without modifying other symbols");
        replacement.ProcessRightClick(1); await Idle();
        Check(replacement.Topmost && replacement.IsFixed && ((Border)replacement.FindName("Card")).ContextMenu is null, "single right click fixes directly without context menu");
        var handle = new WindowInteropHelper(replacement).Handle;
        GetWindowRect(handle, out var before);
        SetWindowPos(handle, IntPtr.Zero, before.Left + 90, before.Top + 90, 0, 0, 0x0001 | 0x0004);
        GetWindowRect(handle, out var after);
        Check(before.Left == after.Left && before.Top == after.Top, "fixed native position cannot move");
        replacement.ProcessRightClick(1); await Idle();
        SetWindowPos(handle, IntPtr.Zero, before.Left + 35, before.Top + 35, 0, 0, 0x0001 | 0x0004);
        GetWindowRect(handle, out after);
        Check(!replacement.Topmost && after.Left == before.Left + 35, "Free restores normal z-order and movement");
        replacement.SetNameVisible(true); await Idle();
        Check(replacement.NameVisible, "name visible when enabled");
        Check(replacement.ActualHeight <= 32 && replacement.ActualWidth < 300, "compact single-line quote dimensions");
        Check(VisualChildren<TextBlock>(replacement).Count(t => t.IsVisible) == 3 && replacement.FindName("StockCode") is null && replacement.FindName("QuoteStatus") is null,
            "quote contains only name, price and percentage");
        var nameText = (TextBlock)replacement.FindName("StockName");
        var priceText = (TextBlock)replacement.FindName("PriceText");
        var rateText = (TextBlock)replacement.FindName("PercentText");
        Check(Math.Abs(nameText.TranslatePoint(new Point(0, nameText.ActualHeight / 2), replacement).Y
            - priceText.TranslatePoint(new Point(0, priceText.ActualHeight / 2), replacement).Y) < 1
            && Math.Abs(rateText.TranslatePoint(new Point(0, rateText.ActualHeight / 2), replacement).Y
            - priceText.TranslatePoint(new Point(0, priceText.ActualHeight / 2), replacement).Y) < 1,
            "name, price and percentage share one horizontal row");
        replacement.ApplyQuote(new(samsung.Code, 72500, -1.25m, DateTimeOffset.UtcNow, "091501"));
        Check(replacement.DisplayedPrice == "72,500원" && ((TextBlock)replacement.FindName("PercentText")).Text.Contains("-1.25%"), "Korean price and daily percentage formatting");
        replacement.ApplyQuote(new(samsung.Code, 1, 0, DateTimeOffset.UtcNow));
        Check(replacement.DisplayedPrice == "72,500원", "late REST snapshot cannot overwrite live tick");
        var sourceTime = DateTimeOffset.Parse("2026-10-30T15:30:00+09:00");
        replacement.ApplyQuote(new(samsung.Code, 73000, 1.5m, DateTimeOffset.UtcNow, "153000", Source: "네이버 공개 시세 · KRX", SourceTime: sourceTime, MarketStatus: "CLOSE"));
        replacement.SetConnection(new("공개 시세", true));
        Check(replacement.StatusMessage.Contains("10.30") && replacement.StatusMessage.Contains("장 마감"), "public tooltip preserves provider date and closed state");
        replacement.ApplyQuote(new(samsung.Code, 1, 0, DateTimeOffset.UtcNow, "152900", SourceTime: sourceTime.AddMinutes(-1)));
        Check(replacement.DisplayedPrice == "73,000원", "older public source timestamp cannot overwrite a newer quote");
        replacement.SetConnection(new("끊김"));
        Check(replacement.StatusMessage.Contains("마지막 수신") && ((Border)replacement.FindName("Card")).Opacity < 1, "disconnection dims old data and explains in tooltip");
        var search = (TextBox)main.FindName("SearchBox"); main.Activate(); search.Focus(); search.Text = "삼성"; await Idle();
        Check(((ListBox)main.FindName("SearchResults")).Items.Count >= 3 && ((Popup)main.FindName("Suggestions")).IsOpen, "typing opens autocomplete dropdown");
        Check(search.GetRectFromCharacterIndex(0).Height >= 12, "search text has a complete text line");
        SaveImage(main, "search-autocomplete.png");
        ((Popup)main.FindName("Suggestions")).IsOpen = false;
        replacement.SetConnection(new("연결", true)); replacement.SetFixed(true); await Idle();
        SaveImage(replacement, "quote-fixed.png");
        var namedWidth = replacement.ActualWidth;
        replacement.SetNameVisible(false); await Idle(); SaveImage(replacement, "quote-price-only.png");
        Check(replacement.ActualWidth < namedWidth && replacement.ActualHeight <= 32, "hiding name also shrinks popup width");
        main.WindowState = WindowState.Minimized; await Idle();
        Check(replacement.IsVisible && replacement.WindowState == WindowState.Normal, "minimizing search keeps independent price strip visible");
        main.WindowState = WindowState.Normal; await Idle();
        var settings = new SettingsWindow("공개 시세 연결") { Owner = main }; settings.Show(); await Idle();
        SaveImage(settings, "settings.png");
        Check(!VisualChildren<PasswordBox>(settings).Any() && settings.FindName("AppKeyInput") is null && settings.FindName("SecretInput") is null,
            "settings contain no API key fields");
        settings.Close();
        second.ProcessRightClick(1); second.ProcessRightClick(2); await Idle();
        Check(!second.IsVisible && main.Popups.Count == 1, "double right click closes popup and removes its subscription");
        main.Close(); await Idle(); Check(!replacement.IsVisible && main.Popups.Count == 0, "main close closes all quote windows");
    }
    private static async Task Until(Func<bool> condition)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (!condition()) await Task.Delay(15, limit.Token);
    }
    private static async Task Idle() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(40); }
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }
    private static void SaveImage(Window window, string file)
    {
        var dir = Path.GetFullPath("artifacts/ui"); Directory.CreateDirectory(dir);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, file)); png.Save(stream);
    }
    private static string Tick(string code, string price, string percent) => JsonSerializer.Serialize(new
    { trnm = "REAL", data = new[] { new { type = "0B", item = code, values = new Dictionary<string, string> { ["10"] = price, ["12"] = percent, ["20"] = "091501" } } } });
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private sealed class PublicHttpFixture : HttpMessageHandler
    {
        public int QuoteRequests, LastBatchSize;
        public int Interval = 2000;
        public bool HadAuthorization, RateLimited;
        public List<DateTimeOffset> RequestTimes { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            HadAuthorization |= request.Headers.Authorization is not null;
            var uri = request.RequestUri!;
            object body;
            if (uri.Host == "ac.stock.naver.com")
                body = new { items = new[] {
                    new { code = "005930", name = "삼성전자", typeCode = "KOSPI", nationCode = "KOR" },
                    new { code = "0000A0", name = "예시 ETF", typeCode = "ETF", nationCode = "KOR" },
                    new { code = "AAPL", name = "애플", typeCode = "NASDAQ", nationCode = "USA" } } };
            else if (uri.Host == "m.stock.naver.com")
                body = new { stocks = new[] { new { itemCode = uri.AbsolutePath.Contains("KOSDAQ") ? "196170" : "005930", stockName = "검색 종목" } } };
            else if (uri.Host == "polling.finance.naver.com")
            {
                QuoteRequests++; RequestTimes.Add(DateTimeOffset.UtcNow);
                if (RateLimited)
                {
                    var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                    return Task.FromResult(limited);
                }
                var codes = uri.Segments.Last().Split(','); LastBatchSize = codes.Length;
                body = new { pollingInterval = Interval, datas = codes.Select(code => new {
                    itemCode = code, closePrice = "72,500", fluctuationsRatio = "1.25", compareToPreviousPrice = new { code = "5" },
                    localTradedAt = "2026-10-30T15:30:00+09:00", marketStatus = "CLOSE" }).ToArray() };
            }
            else throw new Exception("Unexpected public host.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class FakeSocket : IQuoteSocket
    {
        private readonly Channel<string> incoming = Channel.CreateUnbounded<string>();
        public ConcurrentQueue<string> Sent { get; } = new();
        public Uri? Endpoint { get; private set; }
        public void Push(string message) => incoming.Writer.TryWrite(message);
        public Task ConnectAsync(Uri uri, CancellationToken cancellation) { Endpoint = uri; return Task.CompletedTask; }
        public Task SendAsync(string json, CancellationToken cancellation)
        {
            Sent.Enqueue(json);
            var kind = KiwoomParser.Text(Json(json), "trnm");
            if (kind is "REG" or "REMOVE") Push(JsonSerializer.Serialize(new { trnm = kind, return_code = 0 }));
            return Task.CompletedTask;
        }
        public async Task<string> ReceiveAsync(CancellationToken cancellation) => await incoming.Reader.ReadAsync(cancellation);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeHttp : HttpMessageHandler
    {
        public int AuthCalls, ContinuationRequests;
        public bool Reject, RepeatContinuation;
        public bool ValidHeaders = true, OnlyReadEndpoints = true;
        public HashSet<string> Markets { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var api = request.Headers.GetValues("api-id").Single();
            OnlyReadEndpoints &= request.RequestUri!.AbsolutePath is "/oauth2/token" or "/api/dostk/stkinfo";
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            object payload;
            if (api == "au10001")
            {
                AuthCalls++;
                payload = Reject ? new { return_code = 3, return_msg = "rejected" } : (object)new
                { return_code = 0, token = "fixture-token", expires_dt = DateTimeOffset.UtcNow.AddDays(1).ToOffset(TimeSpan.FromHours(9)).ToString("yyyyMMddHHmmss") };
            }
            else
            {
                ValidHeaders &= request.Headers.Authorization?.ToString() == "Bearer fixture-token" && request.Headers.Contains("cont-yn") && request.Headers.Contains("next-key");
                var body = Json(await request.Content!.ReadAsStringAsync(cancellation));
                if (api == "ka10099")
                {
                    var market = KiwoomParser.Text(body, "mrkt_tp"); Markets.Add(market);
                    var continuation = request.Headers.GetValues("cont-yn").Single() == "Y";
                    if (continuation) ContinuationRequests++;
                    if (market == "0" && (!continuation || RepeatContinuation)) { response.Headers.Add("cont-yn", "Y"); response.Headers.Add("next-key", "page2"); }
                    payload = new { return_code = 0, list = new[] { new { code = continuation ? "0000A0" : "005930", name = continuation ? "예시ETF" : "삼성전자" } } };
                }
                else payload = new { return_code = 0, cur_prc = "-72500", flu_rt = "-1.25" };
            }
            response.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return response;
        }
    }
}
