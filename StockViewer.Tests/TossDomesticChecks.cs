using System.Net;
using System.Text;
using System.Text.Json;
using StockViewer.Core;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static JsonElement DomesticCalendar(DateTimeOffset now, bool holiday = false)
    {
        object Day(DateTimeOffset day) => new
        {
            date = day.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd"),
            krxEntireTradingHours = new Dictionary<string, string?> {
                ["officialExtendedStartTime"] = holiday ? null : "08:30:00", ["preLastPriceOrderEndTime"] = holiday ? null : "08:40:00",
                ["startTime"] = holiday ? null : "09:00:00", ["endTime"] = holiday ? null : "15:30:00",
                ["afterExtraSinglePriceOrderStartTime"] = holiday ? null : "16:00:00", ["afterMarketEndTime"] = holiday ? null : "20:00:00" },
            nxtEntireTradingHours = new Dictionary<string, string?> {
                ["preMarketStartTime"] = holiday ? null : "08:00:00", ["preMarketEndTime"] = holiday ? null : "08:50:00",
                ["startTime"] = holiday ? null : "09:00:30", ["endTime"] = holiday ? null : "15:20:00",
                ["afterMarketSinglePriceEndTime"] = holiday ? null : "15:40:00", ["afterMarketEndTime"] = holiday ? null : "20:00:00",
                ["closingPriceStartTime"] = holiday ? null : "15:30:00", ["closingPriceEndTime"] = holiday ? null : "16:00:00" }
        };
        return Json(JsonSerializer.Serialize(new { prevBizDay = Day(now.AddDays(-1)), today = Day(now), nextBizDay = Day(now.AddDays(1)) }));
    }
    private sealed class DomesticHttpFixture(DayClock clock) : HttpMessageHandler
    {
        public int InitRequests, CalendarRequests, InfoRequests, PriceRequests;
        public int InfoDelayMs, ConcurrentInfo, MaxConcurrentInfo;
        public bool FailPrices, FailCalendar, Limited, HadPrivateRequest;
        public string? MissingCode, WrongSymbol;
        public decimal Price = 10200;
        public TaskCompletionSource? HoldPrices;
        public TaskCompletionSource PriceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (request.Headers.Authorization is not null || request.Method != HttpMethod.Get
                || uri.Host is not ("wts-api.tossinvest.com" or "wts-info-api.tossinvest.com") || !request.Headers.Contains("browser-tab-id"))
            { HadPrivateRequest = true; throw new Exception("Unexpected domestic request"); }
            object result;
            if (uri.AbsolutePath == "/api/v3/init") { Interlocked.Increment(ref InitRequests); result = new { }; }
            else if (uri.AbsolutePath == "/api/v2/system/trading-hours/integrated")
            {
                Interlocked.Increment(ref CalendarRequests);
                if (FailCalendar) return new(HttpStatusCode.ServiceUnavailable);
                result = new { ats = DomesticCalendar(clock.Now) };
            }
            else if (uri.AbsolutePath.StartsWith("/api/v2/stock-infos/code-or-symbol/"))
            {
                Interlocked.Increment(ref InfoRequests); var symbol = uri.Segments.Last();
                var active = Interlocked.Increment(ref ConcurrentInfo);
                int previous;
                do { previous = Volatile.Read(ref MaxConcurrentInfo); }
                while (previous < active && Interlocked.CompareExchange(ref MaxConcurrentInfo, active, previous) != previous);
                try { if (InfoDelayMs > 0) await Task.Delay(InfoDelayMs, cancellationToken); }
                finally { Interlocked.Decrement(ref ConcurrentInfo); }
                result = new { code = "TOSS" + symbol, symbol = symbol == WrongSymbol ? "999999" : symbol,
                    currency = "KRW", market = new { code = "KSP" }, nxtSupported = symbol != "069500" };
            }
            else if (uri.AbsolutePath.StartsWith("/api/v1/mds/info/krx-extended-hour-support-yn/"))
                result = new { shortCode = uri.Segments.Last(), date = clock.Now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd"),
                    afterMarketPossYn = uri.Segments.Last() != "TOSS069500" };
            else if (uri.AbsolutePath == "/api/v5/stock-prices")
            {
                Interlocked.Increment(ref PriceRequests); PriceStarted.TrySetResult();
                if (HoldPrices is { } hold) await hold.Task.WaitAsync(cancellationToken);
                if (Limited)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30)); return response;
                }
                if (FailPrices) return new(HttpStatusCode.ServiceUnavailable);
                var codes = Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("productCodes="))[13..]).Split(',');
                result = new { prices = codes.Where(c => c != "TOSS" + MissingCode).Select(c => new {
                    productCode = c, marketCurrency = "KRW", exchange = c == "TOSS069500" ? "krx" : "integrated",
                    close = Price, @base = 10000, dt = clock.Now.AddSeconds(-1).ToString("O") }).ToArray() };
            }
            else { HadPrivateRequest = true; throw new Exception("Unexpected domestic URL: " + uri.AbsolutePath); }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result }), Encoding.UTF8, "application/json") };
        }
    }

    private static async Task TossDomesticTests()
    {
        var clock = new DayClock { Now = DateTimeOffset.Parse("2026-11-11T10:00:00+09:00") };
        var calendar = DomesticCalendar(clock.Now);
        var stock = new TossDomesticProduct("VERIFIED", "005930", true, true);
        var etf = new TossDomesticProduct("ETF", "069500", false, false);
        string Status(string time, TossDomesticProduct? product = null) => TossDomesticMarket.MarketStatus(calendar, product ?? stock,
            DateTimeOffset.Parse("2026-11-11T" + time + "+09:00"));
        Check(Status("08:10:00") == "OPEN" && Status("08:10:00", etf) == "CLOSE", "Toss NXT premarket opens only supported domestic stocks");
        Check(Status("10:00:00") == "OPEN" && Status("10:00:00", etf) == "OPEN", "Toss KRX-only ETFs remain open during regular trading");
        Check(Status("18:00:00") == "OPEN" && Status("18:00:00", etf) == "CLOSE", "Toss after-hours honours NXT support and KRX extended eligibility per product");
        Check(Status("18:00:00", stock with { NxtSupported = false }) == "OPEN", "KRX extended trading also works without NXT coverage");
        Check(Status("20:00:00") == "CLOSE" && Status("07:59:59") == "CLOSE", "Toss full close and pre-opening boundaries follow the actual provider calendar");
        Check(TossDomesticMarket.MarketStatus(DomesticCalendar(clock.Now, true), stock, clock.Now) == "CLOSE", "an explicit holiday calendar never invents an open domestic market");
        Check(TossDomesticMarket.MarketStatus(default, stock, clock.Now) == "UNKNOWN"
            && Status("18:00:00", stock with { NxtSupported = false, KrxAfterSupported = null }) == "UNKNOWN",
            "missing calendar or relevant eligibility is uncertain instead of a false full close");
        var frame = Json("{\"prices\":[{\"productCode\":\"VERIFIED\",\"marketCurrency\":\"KRW\",\"exchange\":\"integrated\",\"close\":9800,\"base\":10000,\"dt\":\"2026-11-11T09:59:59+09:00\"}]}");
        var mapping = new Dictionary<string, TossDomesticProduct> { [stock.Code] = stock };
        var parsed = TossDomesticMarket.ParsePrices(frame, mapping, calendar, clock.Now).Single();
        Check(parsed is { Code: "005930", Price: 9800, ChangePercent: -2, Currency: "KRW", MarketStatus: "OPEN" }
            && parsed.Source.Contains("KRX+NXT 통합") && parsed.SourceTime != parsed.ReceivedAt,
            "Toss integrated price preserves its own exchange, real source time and signed previous-close return");
        foreach (var invalid in new[] { frame.GetRawText().Replace("KRW", "USD"), frame.GetRawText().Replace("9800", "0"),
            frame.GetRawText().Replace("10000", "0"), frame.GetRawText().Replace("09:59:59", "11:00:00"), frame.GetRawText().Replace("integrated", "unknown"),
            frame.GetRawText().Replace("VERIFIED", "WRONG") })
            Check(TossDomesticMarket.ParsePrices(Json(invalid), mapping, calendar, clock.Now).Length == 0, "invalid Toss domestic rows cannot become fabricated prices");
        var handler = new DomesticHttpFixture(clock);
        using (var feed = new TossDomesticMarket(handler, clock))
        {
            var quotes = await feed.GetQuotesAsync(["005930", "069500"]);
            Check(quotes.Quotes.Count == 2 && quotes.Quotes["005930"].Source.Contains("통합") && quotes.Quotes["069500"].Source.EndsWith("KRX"),
                "anonymous Toss maps domestic stocks and ETFs without assuming a product-code prefix");
            await feed.GetQuotesAsync(["005930", "069500"]);
            Check(handler.InitRequests == 1 && handler.InfoRequests == 2 && handler.CalendarRequests == 1 && handler.PriceRequests == 2 && !handler.HadPrivateRequest,
                "Toss visitor, product mappings and calendar are reused without account, login or order requests");
        }
        var partial = new DomesticHttpFixture(clock) { WrongSymbol = "000660" };
        using (var feed = new TossDomesticMarket(partial, clock))
        {
            var result = await feed.GetQuotesAsync(["005930", "000660"]);
            Check(result.Quotes.ContainsKey("005930") && result.Errors.ContainsKey("000660"), "one invalid Toss mapping does not discard other domestic prices");
        }
        var workspace = new DomesticHttpFixture(clock) { InfoDelayMs = 30 };
        using (var feed = new TossDomesticMarket(workspace, clock))
        {
            var result = await feed.GetQuotesAsync(Enumerable.Range(100000, 20).Select(n => n.ToString()));
            Check(result.Quotes.Count == 20 && workspace.PriceRequests == 1 && workspace.MaxConcurrentInfo is > 1 and <= 4,
                "twenty initial domestic products use bounded parallel metadata lookup and one batch of valid prices");
        }
        var unavailable = new DomesticHttpFixture(clock) { FailCalendar = true };
        var initial = new DomesticHttpFixture(clock) { InfoDelayMs = 180 };
        await using (var initialService = new PublicMarketData(new PublicHttpFixture(), domesticHandler: initial, domesticClock: clock))
        {
            var stocks = Enumerable.Range(100000, 20).Select(n => new Stock(n.ToString(), "배치 " + n, "KOSPI")).ToArray();
            var main = MakeMain();
            try
            {
                await main.UseServiceAsync(initialService, stocks);
                var windows = await Task.WhenAll(stocks.Select(main.OpenStockAsync));
                Check(windows.All(w => w?.LastQuote?.Source.Contains("토스") == true) && main.Popups.Count == 20 && main.DataDelayNotice is null,
                    "twenty initial popup requests share due Toss subscription batches and beat the first-price deadline despite delayed metadata");
            }
            finally { main.Close(); await Idle(); }
        }
        using (var feed = new TossDomesticMarket(unavailable, clock))
        {
            var result = await feed.GetQuotesAsync(["005930"]);
            Check(result.Quotes["005930"].MarketStatus == "UNKNOWN", "calendar failure still delivers the real price without greying it as closed");
        }
        var limited = new DomesticHttpFixture(clock) { Limited = true };
        using (var feed = new TossDomesticMarket(limited, clock))
        {
            await Throws<IOException>(async () => await feed.GetQuotesAsync(["005930"]), "Toss domestic rate limits do not fall back to a different price source");
            await Throws<IOException>(async () => await feed.GetQuotesAsync(["005930"]), "Toss domestic Retry-After prevents repeated requests");
            Check(limited.PriceRequests == 1, "Toss domestic cooldown is shared by price requests");
        }
        var held = new DomesticHttpFixture(clock) { HoldPrices = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using (var feed = new TossDomesticMarket(held, clock))
        using (var cancel = new CancellationTokenSource())
        {
            var pending = feed.GetQuotesAsync(["005930"], cancel.Token);
            await held.PriceStarted.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel();
            await Throws<OperationCanceledException>(async () => await pending, "canceling a first-price request stops Toss domestic network work");
        }
        var domestic = new DomesticHttpFixture(clock);
        var primary = new PublicHttpFixture { FailDomestic = true };
        await using var service = new PublicMarketData(primary, domesticHandler: domestic, domesticClock: clock);
        var quote = await service.GetSnapshotAsync("005930");
        Check(quote.Source.Contains("토스") && primary.DomesticQuoteRequests == 0, "the production service uses Toss exclusively for domestic prices");
        Quote? latest = null; ConnectionStatus? status = null;
        service.QuoteReceived += q => Volatile.Write(ref latest, q);
        service.StatusChanged += s => Volatile.Write(ref status, s);
        await service.SetSubscriptionsAsync(["005930"]); service.Start();
        await Until(() => Volatile.Read(ref latest) is { Price: 10200 });
        domestic.FailPrices = true;
        await Until(() => Volatile.Read(ref status)?.Connected == false);
        Check(latest!.Price == 10200 && latest.MarketStatus == "OPEN", "a Toss network failure retains the last real price without pretending the market closed");
        domestic.FailPrices = false; domestic.MissingCode = "005930";
        await Task.Delay(1100);
        Check(latest.Price == 10200, "a missing Toss row keeps the last price instead of replacing it with zero");
        domestic.MissingCode = null; domestic.Price = 10300;
        await Until(() => Volatile.Read(ref latest)?.Price == 10300 && Volatile.Read(ref status)?.Connected == true);
        Check(primary.DomesticQuoteRequests == 0, "Toss recovers from network errors and missing rows without silently switching to Naver domestic prices");
    }
}
