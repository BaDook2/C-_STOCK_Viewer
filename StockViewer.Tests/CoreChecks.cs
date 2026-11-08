using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using StockViewer.Core;

namespace StockViewer.Tests;

internal static partial class Program
{
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
