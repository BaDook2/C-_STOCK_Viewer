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
using System.Windows.Input;
using System.Windows.Documents;
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
                if (args.Contains("--right-click-check")) await RightClickCheck();
                else if (args.Contains("--public-smoke")) await LivePublicSmoke();
                else if (args.Contains("--quote-refresh-check")) await QuoteRefreshTests();
                else if (args.Contains("--polling-check")) { await FastPollingTests(); await QuoteRefreshTests(); await ScopedStatusTests(); }
                else { await CoreTests(); await TransportTests(); await PublicDataTests(); await UnitedStatesDataTests(); await FastPollingTests(); await QuoteRefreshTests(); await RightClickReleaseTests(); await ChordGestureTests(); await UiTests(); await UnitedStatesUiTests(); await AutocompleteFocusTests(); await SearchInputTests(); await WindowLifetimeTests(); await SearchRecallTests(); await ScopedStatusTests(); }
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
        Check(StockCode.IsValid("US:AAPL.O") && StockCode.IsValid("US:BRKb") && StockCode.IsValid("US:SPY"), "US provider identifiers retain their exact spelling");
        Check(!StockCode.IsValid("US:") && !StockCode.IsValid("US:AAPL.O/other") && !StockCode.IsValid("US:AAPL,SPY")
            && !StockCode.IsValid("US:AAPL?x=1"), "invalid US identifiers cannot change request paths or batches");
        Check(StockCode.IsDomestic("ABCDEF") && StockCode.IsUnitedStates("US:ABCDEF") && !StockCode.IsDomestic("US:ABCDEF"), "six-letter US tickers cannot collide with domestic codes");
        var usStocks = new Stock[] { new("US:AAPL.O", "애플", "NASDAQ", "AAPL"), new("US:BRKb", "버크셔 해서웨이 Class B", "NYSE", "BRK B") };
        Check(StockSearch.Find(usStocks, "aapl").Single().Code == "US:AAPL.O" && StockSearch.Find(usStocks, "애플").Single().DisplayCode == "AAPL", "US ticker and Korean name work in cached search");
        Check(StockSearch.Find(usStocks, "brkb").Single().Code == "US:BRKb" && StockSearch.IsExactMatch(usStocks[1], "brk b"), "class-share symbols normalize spaces and case without modifying provider IDs");
        var oldStock = JsonSerializer.Deserialize<Stock>("{\"Code\":\"005930\",\"Name\":\"삼성전자\",\"Market\":\"KOSPI\"}")!;
        Check(oldStock.DisplayCode == "005930" && JsonSerializer.Deserialize<Stock>(JsonSerializer.Serialize(usStocks[1])) == usStocks[1], "old domestic and new US catalog caches are compatible");
        var quote = KiwoomParser.Snapshot("005930", Json("{\"cur_prc\":\"-72,500\",\"flu_rt\":\"-1.25\"}"));
        Check(quote.Price == 72500 && quote.ChangePercent == -1.25m, "negative price sign normalized; negative percent retained");
        var tick = KiwoomParser.Realtime(Json(Tick("005930", "+73500", "+2.50"))).Single();
        Check(tick.Price == 73500 && tick.ChangePercent == 2.50m && tick.TradeTime == "091501", "0B trade fields");
        Check(!KiwoomParser.Realtime(Json("{\"trnm\":\"PING\"}")).Any(), "ignore non-trade messages");
        Check(!KiwoomParser.Realtime(Json(Tick("005930", "bad", "+2.50"))).Any(), "ignore malformed trade row");
        Check(new ApiCredentials("sensitive-app", "sensitive-secret").ToString().Contains("hidden"), "credentials ToString redacted");
        var below = PopupPlacement.BelowAnchor(new System.Windows.Rect(500, 100, 170, 32), new Size(286, 80), new System.Windows.Rect(0, 0, 1920, 1040));
        Check(below == new Point(442, 136), "search is centered below the quote with a four-DIP gap");
        var above = PopupPlacement.BelowAnchor(new System.Windows.Rect(500, 1000, 170, 32), new Size(286, 80), new System.Windows.Rect(0, 0, 1920, 1040));
        Check(above.Y == 916 && above.X == 442, "search flips above a quote near the bottom work-area edge");
        var negative = PopupPlacement.BelowAnchor(new System.Windows.Rect(-1600, 60, 170, 32), new Size(286, 80), new System.Windows.Rect(-1600, 0, 1600, 900));
        Check(negative.X == -1600 && negative.Y == 96, "a monitor left of the primary screen clamps search to its own left edge");
        var rightEdge = PopupPlacement.BelowAnchor(new System.Windows.Rect(-170, 60, 170, 32), new Size(286, 80), new System.Windows.Rect(-1600, 0, 1600, 900));
        Check(rightEdge.X + 286 == 0, "search stays within the source monitor's right edge");
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
        Check(results.Count == 6 && results.Any(s => s.Code == "0000A0") && results.Any(s => s.Code == "US:AAPL.O" && s.DisplayCode == "AAPL"), "public autocomplete includes domestic and US stocks");
        Check(results.Any(s => s.Code == "US:SPY") && results.Any(s => s.Code == "US:BRKb" && s.DisplayCode == "BRK B")
            && results.All(s => !s.Name.StartsWith("invalid")), "US ETFs and share classes use provider IDs; malformed and unsupported markets are ignored");
        var first = await service.GetSnapshotAsync("005930");
        var repeated = await service.GetSnapshotAsync("005930");
        Check(first.Price == 72500 && first.ChangePercent == -1.25m, "public price and direction parse correctly");
        Check(first.SourceTime.HasValue && first.MarketStatus == "CLOSE" && first.Source.Contains("네이버"), "public source timestamp and closed market retained");
        Check(handler.QuoteRequests == 1 && repeated == first, "initial lookup and repeated popup share cached quote");
        Check(!handler.HadAuthorization, "public requests send no authorization or broker key");
        Check(PublicMarketData.DefaultInterval == TimeSpan.FromSeconds(1), "fast polling targets one second");
        Check(PublicMarketData.ReadPollingInterval(Json("{\"pollingInterval\":70000}")) == TimeSpan.FromSeconds(70), "provider hint is retained as metadata");
        Check(PublicMarketData.ReadPollingInterval(Json("{\"pollingInterval\":\"bad\"}")) == TimeSpan.FromSeconds(1), "bad interval metadata cannot suppress valid prices");
        Check(PublicMarketData.ParseQuotes(Json("{\"datas\":[{},null,{\"itemCode\":\"005930\",\"closePrice\":\"0\",\"fluctuationsRatio\":\"1\"}]}")).Length == 0,
            "invalid public prices never appear as zero");
        var pollingHandler = new PublicHttpFixture();
        await using var polling = new PublicMarketData(pollingHandler);
        var delivered = new ConcurrentDictionary<string, Quote>();
        polling.QuoteReceived += q => delivered[q.Code] = q;
        await polling.SetSubscriptionsAsync(["005930", "000660", "005930"]); polling.Start();
        await Until(() => delivered.Count == 2);
        Check(pollingHandler.QuoteRequests >= 1 && pollingHandler.LastBatchSize == 2, "all popups share one batched public request");
        await Until(() => pollingHandler.RequestTimes.Count >= 2);
        var requestTimes = pollingHandler.RequestTimes.ToArray();
        Check(requestTimes[1] - requestTimes[0] >= TimeSpan.FromSeconds(.85), "one-second polling does not create a busy request loop");
        await polling.SetSubscriptionsAsync([]);
        var count = pollingHandler.QuoteRequests; await Task.Delay(100);
        Check(pollingHandler.QuoteRequests == count, "last popup stops public polling");
        var slowHandler = new PublicHttpFixture { Interval = 70000 };
        await using var slow = new PublicMarketData(slowHandler);
        await slow.GetSnapshotAsync("005930"); await slow.GetSnapshotAsync("005930");
        Check(slow.PollingInterval.TotalSeconds == 1 && slow.ProviderPollingInterval.TotalSeconds == 70 && slowHandler.QuoteRequests == 1,
            "closed-market hints do not delay the target cadence and duplicate snapshots still share cache");
        var limitedHandler = new PublicHttpFixture { RateLimited = true };
        await using var limited = new PublicMarketData(limitedHandler);
        await Throws<IOException>(async () => await limited.GetSnapshotAsync("005930"), "public rate limit reported");
        await Throws<IOException>(async () => await limited.GetSnapshotAsync("005930"), "rate limit cooldown applies to repeated searches");
        Check(limitedHandler.QuoteRequests == 1, "Retry-After prevents extra network calls");
    }
    private static async Task FastPollingTests()
    {
        await using (var malformed = new PublicMarketData(new PublicHttpFixture { MalformedInterval = true }))
            Check((await malformed.GetSnapshotAsync("005930")).Price > 0, "a valid price survives invalid polling metadata");

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowHandler = new PublicHttpFixture { HoldUs = hold, ChangingPrices = true };
        await using (var independent = new PublicMarketData(slowHandler))
        {
            var quotes = new ConcurrentQueue<Quote>(); independent.QuoteReceived += quotes.Enqueue;
            await independent.SetSubscriptionsAsync(["005930", "US:AAPL.O"]); independent.Start();
            await slowHandler.UsStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Until(() => quotes.Count(q => q.Code == "005930") >= 2);
            Check(!hold.Task.IsCompleted && quotes.All(q => q.Code == "005930"), "a stalled US request cannot block domestic polling");
            Check(quotes.Select(q => q.Price).Distinct().Count() >= 2, "healthy prices really change while the other market is waiting");
            Check((await independent.GetSnapshotAsync("005930").WaitAsync(TimeSpan.FromSeconds(2))).Price > 0,
                "a domestic snapshot remains available during a stalled US request");
            hold.TrySetResult();
            await Until(() => quotes.Any(q => q.Code == "US:AAPL.O"));
            Check(true, "the stalled market resumes without restarting the service");
            await independent.SetSubscriptionsAsync([]);
        }
        var snapshotHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshotHandler = new PublicHttpFixture { HoldUs = snapshotHold };
        await using (var snapshots = new PublicMarketData(snapshotHandler))
        {
            var pending = snapshots.GetSnapshotAsync("US:AAPL.O");
            try
            {
                await snapshotHandler.UsStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check((await snapshots.GetSnapshotAsync("005930").WaitAsync(TimeSpan.FromSeconds(2))).Price > 0,
                    "initial snapshots from different markets do not share a blocking network gate");
            }
            finally { snapshotHold.TrySetResult(); await pending; }
        }
        var partialHandler = new PublicHttpFixture { MissingCode = "000660", ChangingPrices = true };
        await using (var partial = new PublicMarketData(partialHandler))
        {
            var quotes = new ConcurrentQueue<Quote>(); var statuses = new ConcurrentQueue<ConnectionStatus>();
            partial.QuoteReceived += quotes.Enqueue; partial.StatusChanged += statuses.Enqueue;
            await partial.SetSubscriptionsAsync(["005930", "000660"]); partial.Start();
            await Until(() => quotes.Count(q => q.Code == "005930") >= 4);
            Check(partialHandler.DomesticBatches.Count(batch => batch.Contains("000660")) <= 3,
                "only the missing symbol backs off while healthy symbols keep their one-second cadence");
            Check(statuses.All(s => s.Connected || s.Code == "000660"), "a missing symbol never marks another symbol disconnected");
            partialHandler.MissingCode = null;
            await Until(() => statuses.Any(s => s.Code == "000660" && s.Connected));
            Check(quotes.Any(q => q.Code == "000660"), "a temporarily missing symbol automatically recovers without reopening its popup");
            await partial.SetSubscriptionsAsync([]);
        }
        var limitedHandler = new PublicHttpFixture { RateLimited = true, RateLimitSeconds = 1 };
        await using (var limited = new PublicMarketData(limitedHandler))
        {
            var delivered = new TaskCompletionSource<Quote>(TaskCreationOptions.RunContinuationsAsynchronously);
            var rateReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            limited.QuoteReceived += q => delivered.TrySetResult(q);
            limited.StatusChanged += status => { if (!status.Connected && status.Code == "005930") rateReported.TrySetResult(); };
            await limited.SetSubscriptionsAsync(["005930"]); limited.Start();
            await rateReported.Task.WaitAsync(TimeSpan.FromSeconds(3));
            limitedHandler.RateLimited = false;
            await Throws<IOException>(async () => await limited.GetSnapshotAsync("000660"), "fast polling still obeys Retry-After for a different symbol");
            Check(limitedHandler.QuoteRequests == 1, "subscription or snapshot requests cannot bypass the shared rate-limit cooldown");
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var times = limitedHandler.RequestTimes.ToArray();
            Check(times.Length >= 2 && times[1] - times[0] >= TimeSpan.FromSeconds(.85), "polling recovers automatically after Retry-After expires");
            Check(limitedHandler.RequestsRevalidate && !limitedHandler.HadAuthorization, "fast requests revalidate cached responses and still use no API key");
        }
        var cancelHandler = new PublicHttpFixture { HoldUs = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var cancelService = new PublicMarketData(cancelHandler);
        var canceled = cancelService.GetSnapshotAsync("US:AAPL.O");
        await cancelHandler.UsStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancelService.DisposeAsync();
        await Throws<OperationCanceledException>(async () => await canceled, "disposing the service also cancels initial snapshots without an explicit caller token");
    }
    private static JsonElement UsQuoteFrame(object? extended = null) => Json(JsonSerializer.Serialize(new
    {
        datas = new[] { new {
            reutersCode = "AAPL.O", symbolCode = "AAPL", closePrice = "95.00", fluctuationsRatio = "-5.00",
            compareToPreviousPrice = new { code = "5" }, compareToPreviousClosePrice = "-5.00",
            localTradedAt = "2026-10-30T16:00:00-04:00", marketStatus = "CLOSE",
            stockExchangeType = new { name = "NASDAQ", delayTime = 0 }, currencyType = new { code = "USD" }, overMarketPriceInfo = extended
        } }
    }));
    private static async Task UnitedStatesDataTests()
    {
        var regular = PublicMarketData.ParseQuotes(UsQuoteFrame(), true).Single();
        Check(regular.Code == "US:AAPL.O" && regular.Currency == "USD" && regular.Price == 95 && regular.ChangePercent == -5, "regular US price and previous-day percentage retain decimal precision and currency");
        Check(regular.Source.Contains("NASDAQ") && regular.SourceTime.HasValue && regular.MarketStatus == "CLOSE", "US exchange, timestamp and market status are retained");
        var afterHours = new { tradingSessionType = "AFTER_MARKET", overPrice = "96.00", fluctuationsRatio = "1.05", localTradedAt = "2026-10-30T17:00:00-04:00", overMarketStatus = "OPEN" };
        var after = PublicMarketData.ParseQuotes(UsQuoteFrame(afterHours), true).Single();
        Check(after.Price == 96 && after.ChangePercent == -4 && after.Source.Contains("애프터마켓") && after.MarketStatus == "OPEN", "after-hours uses the previous day's close instead of the feed's regular-close percentage");
        var premarket = new { tradingSessionType = "PRE_MARKET", overPrice = "104.50", localTradedAt = "2026-11-02T08:00:00-05:00", overMarketStatus = "OPEN" };
        var pre = PublicMarketData.ParseQuotes(UsQuoteFrame(premarket), true).Single();
        Check(pre.Price == 104.50m && pre.ChangePercent == 10 && pre.Source.Contains("프리마켓"), "premarket uses the last regular close as the previous-day base across a weekend and DST change");
        var oldExtended = afterHours with { localTradedAt = "2026-10-30T15:00:00-04:00" };
        Check(PublicMarketData.ParseQuotes(UsQuoteFrame(oldExtended), true).Single().Price == 95, "an older extended quote cannot replace a newer regular quote");
        var malformedExtended = afterHours with { overPrice = "bad" };
        Check(PublicMarketData.ParseQuotes(UsQuoteFrame(malformedExtended), true).Single().Price == 95, "malformed extended data preserves the valid regular quote");
        var missingBase = UsQuoteFrame(afterHours).GetRawText().Replace("\"compareToPreviousClosePrice\":\"-5.00\"", "\"compareToPreviousClosePrice\":\"bad\"");
        Check(PublicMarketData.ParseQuotes(Json(missingBase), true).Single().Price == 95, "missing after-hours daily base preserves the regular quote instead of using an incorrect percentage");
        Check(PublicMarketData.ParseQuotes(Json(UsQuoteFrame(afterHours).GetRawText().Replace("\"-5.00\"", "\"bad\"")), true).Length == 0,
            "a malformed regular base is never shown as a made-up US daily return");
        Check(PublicMarketData.ParseQuotes(Json(UsQuoteFrame().GetRawText().Replace("\"USD\"", "\"EUR\"")), true).Length == 0, "unexpected foreign currencies cannot be mislabeled as dollars");
        Check(PublicMarketData.ParseQuotes(UsQuoteFrame()).Length == 0, "US response rows cannot leak into the domestic parser");

        var handler = new PublicHttpFixture();
        await using var service = new PublicMarketData(handler);
        var apple = await service.GetSnapshotAsync("US:AAPL.O");
        var repeated = await service.GetSnapshotAsync("US:AAPL.O");
        await service.GetSnapshotAsync("US:BRKb");
        await service.GetSnapshotAsync("US:SPY");
        await service.GetSnapshotAsync("US:ABCDEF");
        Check(apple == repeated && apple.Price == 123.45m && handler.UsQuoteRequests == 4 && handler.DomesticQuoteRequests == 0, "US snapshots and duplicate popups share cached worldstock requests");
        Check(handler.RequestedPaths.Any(p => p.EndsWith("/BRKb")) && handler.RequestedPaths.Any(p => p.EndsWith("/SPY"))
            && handler.RequestedPaths.All(p => !p.Contains("US:")), "requests preserve opaque provider IDs and remove only the internal US namespace");
        Check(!handler.HadAuthorization, "US data also needs no account, token or broker key");

        var mixedHandler = new PublicHttpFixture { UsInterval = 70000 };
        await using var mixed = new PublicMarketData(mixedHandler);
        var delivered = new ConcurrentDictionary<string, Quote>(); mixed.QuoteReceived += q => delivered[q.Code] = q;
        await mixed.SetSubscriptionsAsync(["005930", "US:AAPL.O", "US:SPY", "US:AAPL.O"]); mixed.Start();
        await Until(() => delivered.Count == 3);
        Check(mixedHandler.DomesticQuoteRequests >= 1 && mixedHandler.UsQuoteRequests >= 1
            && mixedHandler.UsBatchSizes.All(count => count == 2), "mixed markets are batched separately without duplicate subscriptions");
        await Until(() => mixedHandler.DomesticQuoteRequests >= 2 && mixedHandler.UsQuoteRequests >= 2);
        Check(true, "both markets refresh rapidly even when the US provider suggests 70 seconds");
        await mixed.SetSubscriptionsAsync([]);
        var requestCount = mixedHandler.QuoteRequests; await Task.Delay(100);
        Check(mixedHandler.QuoteRequests == requestCount, "closing all mixed-market popups stops both feeds");

        var failingHandler = new PublicHttpFixture { FailDomestic = true };
        await using var independent = new PublicMarketData(failingHandler);
        var usDelivered = false; independent.QuoteReceived += q => usDelivered |= q.Code == "US:AAPL.O";
        await independent.SetSubscriptionsAsync(["005930", "US:AAPL.O"]); independent.Start();
        await Until(() => usDelivered);
        Check(failingHandler.DomesticQuoteRequests >= 1 && failingHandler.UsQuoteRequests == 1, "failure of one market still allows the other market's quotes in the same cycle");
        var missingHandler = new PublicHttpFixture { MissingCode = "AAPL.O" };
        await using var missing = new PublicMarketData(missingHandler);
        var missingReported = false; missing.StatusChanged += status => missingReported |= !status.Connected;
        await missing.SetSubscriptionsAsync(["005930", "US:AAPL.O"]); missing.Start();
        await Until(() => missingReported);
        Check((await missing.GetSnapshotAsync("005930")).Price > 0, "a missing US quote is reported without faulting the poller or discarding the domestic quote");
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
        Check(service.PollingInterval == TimeSpan.FromSeconds(1), "live requests use the one-second target");
        var english = await service.SearchAsync("AAPL", limit.Token);
        var korean = await service.SearchAsync("애플", limit.Token);
        var apple = english.Single(s => s.DisplayCode == "AAPL");
        Check(korean.Any(s => s.Code == apple.Code), "live US ticker and Korean-name searches resolve the same provider ID");
        foreach (var query in new[] { "AAPL", "SPY", "BRK" })
        {
            var result = query == "AAPL" ? english : await service.SearchAsync(query, limit.Token);
            var stock = result.First(s => StockCode.IsUnitedStates(s.Code));
            var usQuote = await service.GetSnapshotAsync(stock.Code, limit.Token);
            Check(usQuote.Price > 0 && usQuote.SourceTime.HasValue && usQuote.Currency == "USD", $"live US quote without credentials: {stock.DisplayCode}");
            Console.WriteLine($"US smoke: {stock.DisplayCode}; {usQuote.Source}; market {usQuote.MarketStatus}; target {service.PollingInterval.TotalSeconds:0.#}s; provider hint {service.ProviderPollingInterval.TotalSeconds:0.#}s.");
        }
        Console.WriteLine($"Public smoke: target {service.PollingInterval.TotalSeconds:0.#}s; market {quote.MarketStatus}; no credentials used.");
        var live = new ConcurrentQueue<Quote>(); service.QuoteReceived += live.Enqueue;
        await service.SetSubscriptionsAsync(["005930", apple.Code], limit.Token); service.Start();
        await Until(() => live.Count(q => q.Code == "005930") >= 3 && live.Count(q => q.Code == apple.Code) >= 3);
        await service.SetSubscriptionsAsync([], limit.Token);
        foreach (var code in new[] { "005930", apple.Code })
        {
            var samples = live.Where(q => q.Code == code).OrderBy(q => q.ReceivedAt).Take(3).ToArray();
            Check(samples.Length == 3 && samples[2].ReceivedAt > samples[0].ReceivedAt, $"live background polling repeatedly receives {code}");
            Console.WriteLine($"Live cadence {code}: {(samples[2].ReceivedAt - samples[0].ReceivedAt).TotalSeconds / 2:0.000}s average; source {samples[2].SourceTime:O}; received {samples[2].ReceivedAt:O}.");
        }
    }
    private static Task QuoteRefreshTests()
    {
        var popup = new QuoteWindow(new("US:AAPL.O", "애플", "NASDAQ", "AAPL"), true);
        try
        {
            popup.SetConnection(new("connected", true));
            var received = DateTimeOffset.UtcNow;
            var source = received.AddSeconds(-5);
            popup.ApplyQuote(new("US:AAPL.O", 123.45m, 1, received, "160001", Source: "네이버 공개 시세 · NASDAQ", SourceTime: source, Currency: "USD"));
            popup.ApplyQuote(new("US:AAPL.O", 124, 2, received.AddSeconds(1), Source: "네이버 공개 시세 · NASDAQ", Currency: "USD"));
            Check(popup.DisplayedPrice == "$124.00" && popup.LastQuote?.SourceTime is null,
                "a new public price still updates when its response temporarily omits the trade timestamp");
            Check(popup.StatusMessage.Contains("수신") && popup.StatusMessage.Contains("NASDAQ"), "timestamp-free quotes show their source and receive time");
            popup.ApplyQuote(new("US:AAPL.O", 1, 0, received.AddSeconds(2), "155900", Source: "네이버 공개 시세 · NASDAQ", SourceTime: source.AddSeconds(-1), Currency: "USD"));
            Check(popup.DisplayedPrice == "$124.00", "the last known trade timestamp still rejects stale data after a timestamp-free response");
            Check(popup.StatusMessage.Contains(received.AddSeconds(2).ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss")),
                "receive time advances even when an older trade snapshot correctly leaves the price unchanged");
            popup.ApplyQuote(new("US:AAPL.O", 2, 0, received.AddSeconds(-1), Source: "네이버 공개 시세 · NASDAQ", Currency: "USD"));
            Check(popup.DisplayedPrice == "$124.00", "an older cached response cannot overwrite a newer timestamp-free price");
            popup.ApplyQuote(new("US:AAPL.O", 125, 3, received.AddSeconds(3), "160002", Source: "네이버 공개 시세 · NASDAQ", SourceTime: source.AddSeconds(1), Currency: "USD"));
            Check(popup.DisplayedPrice == "$125.00" && popup.StatusMessage.Contains("수신"), "normal timestamped updates resume and keep the receive-time indication");
            popup.ResetQuote();
            popup.ApplyQuote(new("US:AAPL.O", 126, 4, received, Source: "네이버 공개 시세 · NASDAQ", SourceTime: source.AddDays(-1), Currency: "USD"));
            Check(popup.DisplayedPrice == "$126.00", "resetting a service clears the previous source-time guard");
        }
        finally { popup.Close(); }
        return Task.CompletedTask;
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
        Check(VisualChildren<Button>(main).Count(b => b.IsVisible) == 3 && main.FindName("SettingsButton") is null,
            "only minimize, close and search buttons remain; no settings gear");
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
            && first.Top >= main.Top + main.ActualHeight, "new price popup leaves the search input uncovered immediately");
        var previousTop = main.Top;
        main.Top = SystemParameters.WorkArea.Bottom - main.ActualHeight;
        PopupPlacement.BelowSearch(first, main);
        Check(first.Top + first.ActualHeight <= main.Top, "price popup uses space above search at the bottom of the screen");
        main.Top = previousTop; PopupPlacement.BelowSearch(first, main);
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
        RightClick(replacement, 1); await Idle();
        Check(replacement.Topmost && replacement.IsFixed && ((Border)replacement.FindName("Card")).ContextMenu is null, "single right click fixes directly without context menu");
        var handle = new WindowInteropHelper(replacement).Handle;
        GetWindowRect(handle, out var before);
        SetWindowPos(handle, IntPtr.Zero, before.Left + 90, before.Top + 90, 0, 0, 0x0001 | 0x0004);
        GetWindowRect(handle, out var after);
        Check(before.Left == after.Left && before.Top == after.Top, "fixed native position cannot move");
        RightClick(replacement, 1); await Idle();
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
        Check(Mouse.Captured is null, "autocomplete does not capture the first click intended for the close button");
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
        RightClick(second, 1); RightClick(second, 2); await Idle();
        Check(!second.IsVisible && main.Popups.Count == 1, "double right click closes popup and removes its subscription");
        var ended = 0; main.SessionEnded += (_, _) => ended++;
        main.Activate(); search.Focus(); search.Text = ""; search.Text = "삼성"; await Idle();
        var mainHandle = new WindowInteropHelper(main).Handle;
        Check(((Popup)main.FindName("Suggestions")).IsOpen, "autocomplete is open before the close-button regression check");
        Check(close.ClickMode == ClickMode.Press, "close button acts on the first press without waiting for mouse release");
        close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Idle();
        Check(!main.IsVisible && replacement.IsVisible && main.Popups.Count == 1 && ended == 0,
            "clicking X with autocomplete open closes search and preserves the fixed price popup");
        Check(IsWindow(mainHandle) && !IsWindowVisible(mainHandle) && IsWindow(new WindowInteropHelper(replacement).Handle)
            && !((Popup)main.FindName("Suggestions")).IsOpen,
            "X hides search and autocomplete for recall while retaining the price window");
        var lastQuote = replacement.LastQuote;
        await Until(() => !ReferenceEquals(lastQuote, replacement.LastQuote));
        Check(true, "quotes continue refreshing after the search window closes");
        RightClick(replacement, 2);
        await Until(() => ended == 1);
        Check(main.Popups.Count == 0 && !replacement.IsVisible, "closing the last quote ends the session once");
    }
    private static async Task UnitedStatesUiTests()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "StockViewer-us-" + Guid.NewGuid().ToString("N")));
        store.SaveDisplayName(true);
        var handler = new PublicHttpFixture();
        var service = new PublicMarketData(handler);
        var catalog = await service.SearchAsync("AAPL");
        var main = new MainWindow(store, false); main.Show(); await main.UseServiceAsync(service, catalog);
        var display = (CheckBox)main.FindName("DisplayName");
        var search = (TextBox)main.FindName("SearchBox");
        var results = (ListBox)main.FindName("SearchResults");
        main.Activate(); search.Focus(); search.Text = "aapl";
        await Task.Delay(350); await Idle();
        // A ticker match must win even when another autocomplete row is selected.
        results.SelectedIndex = 0;
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Until(() => main.Popups.TryGetValue("US:AAPL.O", out var popup) && popup.LastQuote is not null);
        var apple = main.Popups["US:AAPL.O"];
        Check(apple.DisplayedPrice == "$123.45" && apple.NameVisible, "Enter on a lowercase US ticker opens the exact dollar-priced stock");
        Check(((TextBlock)apple.FindName("PercentText")).Text == "-1.25%" && apple.ActualHeight <= 32 && apple.ActualWidth < 300,
            "US price and daily percentage fit the existing one-line compact popup");
        SaveImage(apple, "quote-us-named.png");
        apple.ApplyQuote(new("US:AAPL.O", .1234m, 1.5m, DateTimeOffset.UtcNow, "160001", Currency: "USD"));
        Check(apple.DisplayedPrice == "$0.1234", "sub-dollar US prices preserve up to four decimal places");
        apple.ApplyQuote(new("US:AAPL.O", 1234.50m, 1.5m, DateTimeOffset.UtcNow, "160002", Currency: "USD"));
        Check(apple.DisplayedPrice == "$1,234.50", "US dollar prices retain grouping and two minimum decimal places");
        display.IsChecked = false;
        var domestic = (await main.OpenStockAsync(catalog.Single(s => s.Code == "005930")))!;
        var replacement = (await main.OpenStockAsync(catalog.Single(s => s.Code == "US:AAPL.O")))!; await Idle();
        Check(!apple.IsVisible && !replacement.NameVisible && !domestic.NameVisible && domestic.DisplayedPrice == "72,500원" && main.Popups.Count == 2,
            "replacing a US popup captures its name preference and preserves the domestic popup");
        handler.MissingCode = "SPY";
        var unavailable = (await main.OpenStockAsync(catalog.Single(s => s.Code == "US:SPY")))!;
        Check(unavailable.IsVisible && unavailable.LastQuote is null && unavailable.StatusMessage.Contains("조회"),
            "a missing US price leaves an error popup instead of crashing the search window");
        unavailable.Close(); handler.MissingCode = null;
        SaveImage(replacement, "quote-us-price-only.png");
        RightClick(replacement, 1); await Idle();
        Check(replacement.IsFixed && replacement.Topmost, "US popups use the same right-click Fix behavior");
        var ended = 0; main.SessionEnded += (_, _) => ended++;
        main.Close(); await Idle();
        Check(!main.IsVisible && domestic.IsVisible && replacement.IsVisible && ended == 0, "closing search preserves both domestic and US popups");
        var lastQuote = replacement.LastQuote;
        await Until(() => !ReferenceEquals(lastQuote, replacement.LastQuote));
        Check(replacement.DisplayedPrice == "$123.45", "US prices continue updating after search closes");
        RightClick(domestic, 2); await Idle();
        Check(!domestic.IsVisible && replacement.IsVisible && ended == 0, "double-right closing one market preserves the other popup");
        RightClick(replacement, 2); await Until(() => ended == 1);
        Check(main.Popups.Count == 0, "closing the last US popup ends the mixed-market session");
    }
    private static async Task AutocompleteFocusTests()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "StockViewer-focus-" + Guid.NewGuid().ToString("N")));
        var main = new MainWindow(store, false);
        var remote = new DelayedSearchService();
        main.Show(); await main.UseServiceAsync(remote, DemoMarketData.SampleStocks);
        main.Activate();
        var search = (TextBox)main.FindName("SearchBox");
        var dropdown = (Popup)main.FindName("Suggestions");
        var submit = (Button)main.FindName("SearchButton");
        search.Focus(); search.Text = "삼성";
        await Until(() => remote.Requests.Count == 1);
        remote.Requests[0].Reply.SetResult([DemoMarketData.SampleStocks[0]]); await Idle();
        Check(dropdown.IsOpen, "typing intentionally opens autocomplete before selecting a quote");
        submit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Until(() => main.Popups.Count == 1);
        var popup = main.Popups.Values.Single();
        RightClick(popup, 1); RightClick(popup, 2);
        await Until(() => main.Popups.Count == 0);
        main.Activate(); search.Focus(); await Idle();
        Check(!dropdown.IsOpen && search.Text == "삼성전자",
            "closing a quote by right-click does not reopen autocomplete when search focus returns");
        submit.Focus(); search.Focus(); await Idle();
        Check(!dropdown.IsOpen && remote.Requests.Count == 1,
            "focus restoration alone neither opens autocomplete nor starts another search");

        search.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent }); await Idle();
        Check(dropdown.IsOpen, "an intentional left click in search still opens existing suggestions");
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Until(() => remote.Requests.Count == 2);
        remote.Requests[1].Reply.SetResult([DemoMarketData.SampleStocks[0]]); await Idle();
        Check(!dropdown.IsOpen, "a late autocomplete response does not reopen a dismissed dropdown");
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Down)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Idle();
        Check(dropdown.IsOpen, "the Down key intentionally reopens suggestions");
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        search.CaretIndex = search.Text.Length;
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, search, "우")); await Idle();
        Check(dropdown.IsOpen && search.Text == "삼성전자우", "new typing reopens autocomplete after focus-only restoration");
        main.Close(); await Idle();
    }
    private static async Task SearchInputTests()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "StockViewer-input-" + Guid.NewGuid().ToString("N")));
        var main = new MainWindow(store, false);
        var remote = new DelayedSearchService();
        main.Show(); await main.UseServiceAsync(remote, DemoMarketData.SampleStocks);
        main.Activate();
        var search = (TextBox)main.FindName("SearchBox");
        var results = (ListBox)main.FindName("SearchResults");
        var suggestions = (Popup)main.FindName("Suggestions");
        search.Focus(); await Idle();

        search.Text = "삼성";
        await Until(() => remote.Requests.Count == 1);
        remote.Requests[0].Reply.SetResult([DemoMarketData.SampleStocks[0]]); await Idle();
        foreach (var query in new[] { "큐리", "삼성전자", "NAVER", "005930", "SK하이닉스", "한화에어로스페이스" })
        {
            // The released app crashed here: clearing disposed searchCancellation,
            // but the next TextChanged/GotKeyboardFocus tried to cancel it again.
            search.SelectAll(); EditingCommands.Backspace.Execute(null, search); await Idle();
            Check(search.Text.Length == 0 && !suggestions.IsOpen, $"clearing the previous remote query is safe before {query}");
            ((Button)main.FindName("SearchButton")).Focus(); search.Focus(); await Idle();
            foreach (var letter in query)
                TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, search, letter.ToString()));
            await Idle();
            Check(search.Text == query && search.IsKeyboardFocused, $"typing after clear and refocus preserves the full query: {query}");
        }

        // A whitespace query also disposes the previous search without replacing it.
        search.Text = " "; await Idle(); search.Text = "큐리";
        var requestCount = remote.Requests.Count;
        await Until(() => remote.Requests.Count > requestCount);
        remote.Requests[^1].Reply.SetResult([new("115180", "큐리언트", "KOSDAQ"), new("445680", "큐리옥스바이오시스템즈", "KOSDAQ")]);
        await Until(() => results.Items.Count == 2);
        Check(search.Text == "큐리" && suggestions.IsOpen, "remote Quri autocomplete survives an empty/whitespace query");
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Down)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), Environment.TickCount, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Until(() => main.Popups.Count == 1);
        Check(main.Popups.ContainsKey("445680"), "Enter opens the selected Quri remote result after repeated clears");
        main.Popups.Values.Single().Close(); main.Activate(); search.Focus();

        search.Text = "없는종목";
        requestCount = remote.Requests.Count;
        await Until(() => remote.Requests.Count > requestCount);
        remote.Requests[^1].Reply.SetResult([]); await Idle();
        Check(results.Items.Count == 0 && results.SelectedIndex == -1, "an empty remote reply keeps no invalid selection");
        search.Text = "네트워크오류";
        requestCount = remote.Requests.Count;
        await Until(() => remote.Requests.Count > requestCount);
        remote.Requests[^1].Reply.SetException(new IOException("fixture network failure")); await Idle();

        search.Text = "큐";
        requestCount = remote.Requests.Count;
        await Until(() => remote.Requests.Count > requestCount);
        var oldReply = remote.Requests[^1].Reply;
        search.Text = "큐리";
        requestCount = remote.Requests.Count;
        await Until(() => remote.Requests.Count > requestCount);
        oldReply.SetResult([new("000270", "기아", "KOSPI")]); await Idle();
        Check(!results.Items.Cast<Stock>().Any(s => s.Code == "000270"), "a canceled delayed reply cannot overwrite the new query");
        remote.Requests[^1].Reply.SetResult([new("115180", "큐리언트", "KOSDAQ")]); await Idle();
        Check(search.Text == "큐리" && results.Items.Cast<Stock>().Any(s => s.Code == "115180"), "search remains usable after an empty reply, network error and canceled reply");
        search.Text = ""; await Idle(); main.Close(); await Idle();
        Check(!main.IsVisible, "closing immediately after clearing an active remote query does not cancel a disposed source");
    }
    private static async Task WindowLifetimeTests()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "StockViewer-lifetime-" + Guid.NewGuid().ToString("N")));
        var main = new MainWindow(store, false);
        var service = new DelayedQuoteService();
        var ended = 0; main.SessionEnded += (_, _) => ended++;
        main.Show(); await main.UseServiceAsync(service, DemoMarketData.SampleStocks);
        var opening = main.OpenStockAsync(DemoMarketData.SampleStocks[0]);
        await Until(() => main.Popups.Count == 1 && service.SnapshotRequested);
        var popup = main.Popups.Values.Single();
        Check(!opening.IsCompleted && main.IsEnabled && ((TextBox)main.FindName("SearchBox")).IsEnabled,
            "waiting for an initial quote does not disable the search window or textbox");
        ((Button)main.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Idle();
        Check(popup.IsVisible && !service.Disposed && !service.SnapshotCancellation.IsCancellationRequested && ended == 0,
            "closing search during a pending snapshot keeps the quote service and request alive");
        service.Snapshot.SetResult(new("005930", 72500, 1.25m, DateTimeOffset.UtcNow));
        await opening;
        Check(popup.DisplayedPrice == "72,500원", "a pending price lookup completes after the search window is closed");
        service.Publish(new("005930", 73000, 1.5m, DateTimeOffset.UtcNow));
        await Until(() => popup.DisplayedPrice == "73,000원");
        Check(true, "live quote events still reach a popup after search closes");
        RightClick(popup, 2); await Until(() => ended == 1);
        Check(service.Disposed && service.SnapshotCancellation.IsCancellationRequested && main.Popups.Count == 0,
            "closing the final quote cancels requests and disposes the market service before ending the app");

        var empty = new MainWindow(store, false);
        var emptyService = new DelayedQuoteService();
        var emptyEnded = 0; empty.SessionEnded += (_, _) => emptyEnded++;
        empty.Show(); await empty.UseServiceAsync(emptyService, DemoMarketData.SampleStocks);
        ((Button)empty.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Until(() => emptyEnded == 1);
        Check(emptyService.Disposed && !empty.IsVisible, "closing search without quote popups ends the app normally");

        var pendingMain = new MainWindow(store, false);
        var pendingService = new DelayedSearchService();
        pendingMain.Show(); await pendingMain.UseServiceAsync(pendingService, DemoMarketData.SampleStocks);
        var keptQuote = (await pendingMain.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
        pendingMain.Activate();
        var pendingBox = (TextBox)pendingMain.FindName("SearchBox"); pendingBox.Focus();
        pendingBox.Text = "아직목록에없는종목";
        await Until(() => pendingService.Requests.Count == 1);
        ((Button)pendingMain.FindName("SearchButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        ((Button)pendingMain.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Idle();
        pendingService.Requests[0].Reply.SetResult([]); await Idle();
        var pendingDropdown = (Popup)pendingMain.FindName("Suggestions");
        Check(!pendingMain.IsVisible && !pendingDropdown.IsOpen
            && pendingDropdown.ReadLocalValue(Popup.IsOpenProperty) is false && keptQuote.IsVisible,
            "a pending search submission cannot reopen any search popup after X closes the search window");
        RightClick(keptQuote, 2); await Idle();
    }
    private static async Task RightClickReleaseTests()
    {
        foreach (var initiallyFixed in new[] { false, true })
        {
            var popup = new QuoteWindow(DemoMarketData.SampleStocks[0], true) { Left = 180, Top = 160 };
            popup.Show(); await Idle(); popup.SetFixed(initiallyFixed);
            var handle = new WindowInteropHelper(popup).Handle;
            popup.ProcessMouseDown(MouseButton.Right, 1);
            Check(popup.IsFixed == initiallyFixed && popup.IsVisible, "right-down waits for release so a left-button chord can take priority");
            var firstUp = RaiseRightButtonUp(popup);
            Check(firstUp.Handled && popup.IsFixed != initiallyFixed && popup.IsVisible && Mouse.Captured is null, "single right-up toggles Fix/Free, is consumed and releases capture without closing");
            popup.ProcessMouseDown(MouseButton.Right, 2);
            Check(popup.IsVisible && IsWindow(handle) && Mouse.Captured == popup && GetCapture() == handle,
                "second right-down retains the native popup and mouse capture until release");
            var secondUp = RaiseRightButtonUp(popup);
            Check(secondUp.Handled && popup.IsVisible && IsWindow(handle),
                "second right-up is consumed before native window destruction");
            await Idle();
            Check(!popup.IsVisible && !IsWindow(handle) && Mouse.Captured is null && GetCapture() == IntPtr.Zero,
                "popup closes after right-up dispatch and leaves no mouse capture");
        }
    }
    private static async Task ChordGestureTests()
    {
        foreach (var initiallyFixed in new[] { false, true })
        foreach (var first in new[] { MouseButton.Left, MouseButton.Right })
        foreach (var firstRelease in new[] { MouseButton.Left, MouseButton.Right })
        {
            var popup = new QuoteWindow(DemoMarketData.SampleStocks[0], true) { Left = 180, Top = 160 };
            popup.Show(); await Idle(); popup.SetFixed(initiallyFixed);
            var requests = 0; popup.SearchRequested += (_, _) => requests++;
            var handle = new WindowInteropHelper(popup).Handle;
            var start = new Point(popup.Left, popup.Top);
            var second = first == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
            var lastRelease = firstRelease == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
            // Also cover a chord interrupting the second click of a right double-click.
            popup.ProcessMouseDown(first, 2);
            popup.ProcessMouseDown(second, 2);
            popup.ProcessMouseMove(popup.PointToScreen(new Point(80, 60)), true, true);
            Check(popup.IsFixed == initiallyFixed && popup.IsVisible && new Point(popup.Left, popup.Top) == start,
                "either chord press order suppresses Fix, double-right close and dragging");
            var firstUp = RaiseButtonUp(popup, firstRelease); await Idle();
            Check(firstUp.Handled && requests == 0 && Mouse.Captured == popup && GetCapture() == handle,
                "either chord release order retains capture and waits for the other button");
            var lastUp = RaiseButtonUp(popup, lastRelease);
            Check(lastUp.Handled && requests == 0 && popup.IsVisible && IsWindow(handle),
                "the last chord release is consumed before requesting search");
            await Idle();
            Check(requests == 1 && popup.IsFixed == initiallyFixed && popup.IsVisible && Mouse.Captured is null && GetCapture() == IntPtr.Zero,
                "a completed chord requests search exactly once and releases native capture without changing the quote");
            RightClick(popup, 2); await Idle();
            Check(popup.IsVisible && popup.IsFixed != initiallyFixed, "the first right click after a chord ignores a stale double-click count");
            RightClick(popup, 2); await Idle();
            Check(!popup.IsVisible, "two standalone right clicks still close normally after a chord");
        }
        var dragged = new QuoteWindow(DemoMarketData.SampleStocks[0], true) { Left = 220, Top = 200 };
        dragged.Show(); await Idle();
        var dragRequests = 0; dragged.SearchRequested += (_, _) => dragRequests++;
        var dragOrigin = dragged.PointToScreen(Mouse.GetPosition(dragged));
        dragged.ProcessMouseDown(MouseButton.Left);
        dragged.ProcessMouseMove(dragOrigin + new Vector(30, 25), true, false);
        Check(dragged.Left > 220 && dragged.Top > 200, "left-only drag still moves a Free quote without entering a native modal drag loop");
        var dragPosition = new Point(dragged.Left, dragged.Top);
        dragged.ProcessMouseDown(MouseButton.Right);
        dragged.ProcessMouseMove(dragOrigin + new Vector(100, 100), true, true);
        RaiseButtonUp(dragged, MouseButton.Right); RaiseButtonUp(dragged, MouseButton.Left); await Idle();
        Check(dragRequests == 1 && new Point(dragged.Left, dragged.Top) == dragPosition, "pressing right during a left drag stops movement and requests search");
        dragged.SetFixed(true);
        dragged.ProcessMouseDown(MouseButton.Left);
        dragged.ProcessMouseMove(dragOrigin + new Vector(150, 150), true, false); RaiseButtonUp(dragged, MouseButton.Left);
        Check(new Point(dragged.Left, dragged.Top) == dragPosition && dragged.IsFixed, "a fixed quote remains immovable during left drag");
        dragged.ProcessMouseDown(MouseButton.Left); dragged.ProcessMouseDown(MouseButton.Right);
        Mouse.Capture(null);
        RaiseButtonUp(dragged, MouseButton.Left); RaiseButtonUp(dragged, MouseButton.Right); await Idle();
        Check(dragRequests == 1 && dragged.IsVisible && dragged.IsFixed, "unexpected capture loss cancels an unfinished chord without showing search or changing Fix");
        dragged.ProcessMouseDown(MouseButton.Right, otherButtonPressed: true);
        RaiseButtonUp(dragged, MouseButton.Left); RaiseButtonUp(dragged, MouseButton.Right); await Idle();
        Check(dragRequests == 2 && dragged.IsFixed, "a button already held when entering the quote can complete a chord");
        var routedLeft = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        var routedRight = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        dragged.RaiseEvent(routedLeft); dragged.RaiseEvent(routedRight);
        RaiseButtonUp(dragged, MouseButton.Right); RaiseButtonUp(dragged, MouseButton.Left); await Idle();
        Check(routedLeft.Handled && routedRight.Handled && dragRequests == 3 && dragged.IsFixed,
            "the XAML mouse-down and mouse-up handlers route a complete chord and consume both buttons");
        dragged.Close();
    }
    private static async Task ScopedStatusTests()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "StockViewer-status-" + Guid.NewGuid().ToString("N")));
        var handler = new PublicHttpFixture { ChangingPrices = true };
        var main = new MainWindow(store, false);
        main.Show(); await main.UseServiceAsync(new PublicMarketData(handler), DemoMarketData.SampleStocks);
        var healthy = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
        var missing = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!;
        var healthyCard = (Border)healthy.FindName("Card"); var missingCard = (Border)missing.FindName("Card");
        await Until(() => healthy.LastQuote is not null && missing.LastQuote is not null && healthyCard.Opacity == 1 && missingCard.Opacity == 1);
        handler.MissingCode = "000660";
        await Until(() => missingCard.Opacity < 1);
        var previous = healthy.LastQuote!.ReceivedAt;
        await Until(() => healthy.LastQuote!.ReceivedAt > previous);
        Check(healthyCard.Opacity == 1 && missingCard.Opacity < 1, "only the failed popup dims while another popup keeps receiving fresh prices");
        healthy.SetConnection(new("unrelated failure", Code: "000660"));
        Check(healthyCard.Opacity == 1, "a popup independently rejects another symbol's connection status");
        handler.MissingCode = null;
        await Until(() => missingCard.Opacity == 1);
        Check(missing.LastQuote is not null && healthyCard.Opacity == 1, "the failed popup returns to normal when its own quotes recover");
        main.Close(); healthy.Close(); missing.Close(); await Idle();
    }
    private static async Task SearchRecallTests()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "StockViewer-recall-" + Guid.NewGuid().ToString("N")));
        store.SaveDisplayName(true);
        var main = new MainWindow(store, false) { Left = 140, Top = 100 };
        main.Show(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        var first = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
        var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; await Idle();
        first.Left = 400; first.Top = 220; first.SetFixed(true);
        second.Left = 600; second.Top = 400;
        var handle = new WindowInteropHelper(main).Handle;
        var search = (TextBox)main.FindName("SearchBox");
        var display = (CheckBox)main.FindName("DisplayName");
        var suggestions = (Popup)main.FindName("Suggestions");
        main.Activate(); search.Focus(); search.Text = "삼성"; await Idle();
        var quotePosition = new Point(first.Left, first.Top);
        ChordClick(first); await Idle();
        Check(main.IsVisible && new WindowInteropHelper(main).Handle == handle
            && Math.Abs(main.Left + main.ActualWidth / 2 - (first.Left + first.ActualWidth / 2)) < 2
            && main.Top >= first.Top + first.ActualHeight, "a visible search window moves below the clicked quote without creating another window");
        Check(search.Text == "삼성" && display.IsChecked == true && !suggestions.IsOpen && search.IsKeyboardFocused,
            "recall preserves search text and display preference, focuses input and keeps autocomplete closed");
        Check(first.IsFixed && new Point(first.Left, first.Top) == quotePosition && main.Popups.Count == 2,
            "recall leaves fixed quote positions and all popup subscriptions intact");
        var ended = 0; main.SessionEnded += (_, _) => ended++;
        main.Close(); await Idle();
        Check(!main.IsVisible && !IsWindowVisible(handle) && first.IsVisible && second.IsVisible && ended == 0,
            "X hides search while its quotes keep running and can recall it");
        second.Top = SystemParameters.WorkArea.Bottom - second.ActualHeight - 2;
        ChordClick(second, MouseButton.Right, MouseButton.Left); await Idle();
        Check(main.IsVisible && main.Top + main.ActualHeight <= second.Top
            && new WindowInteropHelper(main).Handle == handle,
            "a hidden search window is restored above a bottom-edge quote with the same native window");
        Check(Application.Current.Windows.OfType<MainWindow>().Count() == 1 && ended == 0 && first.LastQuote is not null && second.LastQuote is not null,
            "recall reuses one search window and retains both live quote states");
        main.WindowState = WindowState.Minimized; second.Top = SystemParameters.WorkArea.Top + 180;
        ChordClick(second); await Idle();
        Check(main.WindowState == WindowState.Normal && main.IsVisible && main.Top >= second.Top + second.ActualHeight,
            "a minimized search window is restored near the newly clicked quote");
        SaveImage(main, "search-recalled.png");
        main.Close(); await Idle(); ChordClick(first); await Idle(); main.Close(); await Idle();
        first.Close(); second.Close(); await Until(() => ended == 1);
        Check(!IsWindow(handle), "closing the last quote after hiding search destroys the retained native search window and ends the session");

        var pendingMain = new MainWindow(store, false);
        var remote = new DelayedSearchService();
        pendingMain.Show(); await pendingMain.UseServiceAsync(remote, DemoMarketData.SampleStocks);
        var kept = (await pendingMain.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
        var pendingSearch = (TextBox)pendingMain.FindName("SearchBox");
        pendingMain.Activate(); pendingSearch.Focus(); pendingSearch.Text = "미완성검색";
        await Until(() => remote.Requests.Count == 1);
        ((Button)pendingMain.FindName("SearchButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        pendingMain.Close(); await Idle(); ChordClick(kept); await Idle();
        remote.Requests[0].Reply.SetResult([]); await Idle();
        Check(pendingMain.IsVisible && pendingMain.Popups.Count == 1 && !((Popup)pendingMain.FindName("Suggestions")).IsOpen && remote.Requests.Count == 1,
            "a search submitted before hide cannot open suggestions or a popup after recall");
        kept.Close(); await Idle();
        Check(pendingMain.IsVisible && pendingMain.Popups.Count == 0, "closing the last quote after recall keeps the visible search window available");
        pendingMain.Close(); await Idle();
    }
    private static MouseButtonEventArgs RaiseButtonUp(QuoteWindow popup, MouseButton button)
    {
        var release = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
            { RoutedEvent = Mouse.PreviewMouseUpEvent };
        popup.RaiseEvent(release); return release;
    }
    private static MouseButtonEventArgs RaiseRightButtonUp(QuoteWindow popup) => RaiseButtonUp(popup, MouseButton.Right);
    private static void RightClick(QuoteWindow popup, int clickCount)
    { popup.ProcessMouseDown(MouseButton.Right, clickCount); RaiseRightButtonUp(popup); }
    private static void ChordClick(QuoteWindow popup, MouseButton first = MouseButton.Left, MouseButton firstRelease = MouseButton.Right)
    {
        var second = first == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
        var lastRelease = firstRelease == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
        popup.ProcessMouseDown(first); popup.ProcessMouseDown(second);
        RaiseButtonUp(popup, firstRelease); RaiseButtonUp(popup, lastRelease);
    }
    private static async Task RightClickCheck()
    {
        var status = new TextBlock { Margin = new Thickness(18), FontSize = 14, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Top };
        var host = new Window
        {
            Title = "StockViewer right-click check", Width = 500, Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.White, Content = status
        };
        var finished = new TaskCompletionSource();
        var backgroundUps = 0; var menus = 0; var quoteClosed = false;
        bool? closedWhilePressed = null;
        void Refresh()
        {
            status.Text = $"Background right-button-up: {backgroundUps}\nContext menu openings: {menus}\nQuote closed: {quoteClosed}\nClosed while button pressed: {closedWhilePressed}";
            Console.WriteLine(status.Text.Replace('\n', ';'));
        }
        var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = "Test background menu" });
        host.ContextMenu = menu;
        host.PreviewMouseRightButtonUp += (_, _) => { backgroundUps++; Refresh(); };
        host.ContextMenuOpening += (_, _) => { menus++; Refresh(); };
        QuoteWindow? quote = null;
        host.Closed += (_, _) => { quote?.Close(); finished.TrySetResult(); };
        host.Show(); await Idle();
        quote = new QuoteWindow(DemoMarketData.SampleStocks[0], true);
        quote.ApplyQuote(new("005930", 72500, 1.25m, DateTimeOffset.UtcNow, IsDemo: true));
        quote.Closed += (_, _) => { quoteClosed = true; closedWhilePressed = Mouse.RightButton == MouseButtonState.Pressed; Refresh(); };
        quote.Show(); quote.UpdateLayout();
        quote.Left = host.Left + (host.ActualWidth - quote.ActualWidth) / 2;
        quote.Top = host.Top + 135;
        quote.SetFixed(true);
        Refresh(); await finished.Task;
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
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private sealed class DelayedQuoteService : IMarketDataService
    {
        public bool IsDemo => true;
        public bool Disposed { get; private set; }
        public bool SnapshotRequested { get; private set; }
        public CancellationToken SnapshotCancellation { get; private set; }
        public TaskCompletionSource<Quote> Snapshot { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event Action<Quote>? QuoteReceived;
        public event Action<ConnectionStatus>? StatusChanged;
        public void Start() => StatusChanged?.Invoke(new("fixture", true, true));
        public void Publish(Quote quote) => QuoteReceived?.Invoke(quote);
        public Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
            => Task.FromResult<IReadOnlyList<Stock>>(DemoMarketData.SampleStocks);
        public Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
        { SnapshotRequested = true; SnapshotCancellation = cancellation; return Snapshot.Task.WaitAsync(cancellation); }
        public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class DelayedSearchService : IMarketDataService, IStockSearchService
    {
        public List<(string Query, TaskCompletionSource<IReadOnlyList<Stock>> Reply)> Requests { get; } = [];
        public bool IsDemo => true;
        public event Action<Quote>? QuoteReceived { add { } remove { } }
        public event Action<ConnectionStatus>? StatusChanged;
        public void Start() => StatusChanged?.Invoke(new("fixture", true, true));
        public Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
            => Task.FromResult<IReadOnlyList<Stock>>(DemoMarketData.SampleStocks);
        public Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
            => Task.FromResult(new Quote(code, 72500, 1.25m, DateTimeOffset.UtcNow, IsDemo: true));
        public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Stock>> SearchAsync(string query, CancellationToken cancellation = default)
        {
            // Ignore cancellation to exercise replies already in flight.
            var reply = new TaskCompletionSource<IReadOnlyList<Stock>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((query, reply)); return reply.Task;
        }
        public ValueTask DisposeAsync()
        {
            foreach (var request in Requests) request.Reply.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }
    private sealed class PublicHttpFixture : HttpMessageHandler
    {
        public int QuoteRequests, LastBatchSize, DomesticQuoteRequests, UsQuoteRequests;
        public int Interval = 2000, UsInterval = 2000;
        public volatile bool HadAuthorization, RateLimited, FailDomestic, ChangingPrices, MalformedInterval;
        public volatile bool RequestsRevalidate = true;
        public volatile string? MissingCode;
        public double RateLimitSeconds = 30;
        public TaskCompletionSource? HoldUs;
        public TaskCompletionSource UsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<DateTimeOffset> RequestTimes { get; } = [];
        public ConcurrentQueue<string> RequestedPaths { get; } = [];
        public ConcurrentQueue<int> UsBatchSizes { get; } = [];
        public ConcurrentQueue<string[]> DomesticBatches { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            HadAuthorization |= request.Headers.Authorization is not null;
            if (request.Headers.CacheControl?.NoCache != true) RequestsRevalidate = false;
            var uri = request.RequestUri!;
            object body;
            if (uri.Host == "ac.stock.naver.com")
                body = new { items = new[] {
                    new { code = "005930", name = "삼성전자", typeCode = "KOSPI", nationCode = "KOR", reutersCode = "005930" },
                    new { code = "0000A0", name = "예시 ETF", typeCode = "ETF", nationCode = "KOR", reutersCode = "0000A0" },
                    new { code = "AAPL", name = "애플", typeCode = "NASDAQ", nationCode = "USA", reutersCode = "AAPL.O" },
                    new { code = "SPY", name = "SPDR S&P 500", typeCode = "AMEX", nationCode = "USA", reutersCode = "SPY" },
                    new { code = "BRK B", name = "버크셔 해서웨이 Class B", typeCode = "NYSE", nationCode = "USA", reutersCode = "BRKb" },
                    new { code = "ABCDEF", name = "six-letter ticker", typeCode = "NASDAQ", nationCode = "USA", reutersCode = "ABCDEF" },
                    new { code = "7203", name = "invalid market", typeCode = "TOKYO", nationCode = "JPN", reutersCode = "7203.T" },
                    new { code = "AAPL", name = "invalid missing provider ID", typeCode = "NASDAQ", nationCode = "USA", reutersCode = "" },
                    new { code = "AAPL", name = "invalid provider ID", typeCode = "NASDAQ", nationCode = "USA", reutersCode = "AAPL.O/other" } } };
            else if (uri.Host == "m.stock.naver.com")
                body = new { stocks = new[] { new { itemCode = uri.AbsolutePath.Contains("KOSDAQ") ? "196170" : "005930", stockName = "검색 종목" } } };
            else if (uri.Host == "polling.finance.naver.com")
            {
                var requestNumber = Interlocked.Increment(ref QuoteRequests);
                RequestTimes.Enqueue(DateTimeOffset.UtcNow); RequestedPaths.Enqueue(uri.AbsolutePath);
                var us = uri.AbsolutePath.Contains("/worldstock/");
                if (us) Interlocked.Increment(ref UsQuoteRequests); else Interlocked.Increment(ref DomesticQuoteRequests);
                if (RateLimited)
                {
                    var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(RateLimitSeconds));
                    return limited;
                }
                if (us)
                {
                    UsStarted.TrySetResult();
                    if (HoldUs is { } hold) await hold.Task.WaitAsync(cancellation);
                }
                if (!us && FailDomestic) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                var codes = Uri.UnescapeDataString(uri.Segments.Last()).Split(','); LastBatchSize = codes.Length;
                if (us) UsBatchSizes.Enqueue(codes.Length); else DomesticBatches.Enqueue(codes);
                var price = ChangingPrices ? ((us ? 123.45m : 72500m) + requestNumber).ToString(System.Globalization.CultureInfo.InvariantCulture) : us ? "123.45" : "72,500";
                body = new { pollingInterval = MalformedInterval ? (object)"bad" : us ? UsInterval : Interval, datas = codes.Where(code => code != MissingCode).Select(code => new {
                    itemCode = us ? "" : code, reutersCode = us ? code : "", closePrice = price,
                    fluctuationsRatio = "1.25", compareToPreviousPrice = new { code = "5" },
                    stockExchangeType = new { name = us ? "NASDAQ" : "KRX", delayTime = 0 }, currencyType = new { code = us ? "USD" : "KRW" },
                    localTradedAt = "2026-10-30T15:30:00+09:00", marketStatus = "CLOSE" }).ToArray() };
            }
            else throw new Exception("Unexpected public host.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
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
