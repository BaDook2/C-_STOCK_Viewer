using System.Net;
using System.Text;
using System.Text.Json;
using StockViewer.Core;

namespace StockViewer.Tests;

internal static partial class Program
{
    private sealed class DayClock : TimeProvider
    {
        private long ticks = DateTimeOffset.Parse("2026-11-10T02:30:00Z").UtcTicks;
        public DateTimeOffset Now
        {
            get => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref ticks, value.UtcTicks);
        }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static object DayCalendar() => new
    {
        prevBizDay = new { date = "2026-11-09", dayMarketStart = "10:00:00.000", dayMarketEnd = "17:50:00.000" },
        today = new { date = "2026-11-10", dayMarketStart = "10:00:00.000", dayMarketEnd = "17:50:00.000" },
        nextBizDay = new { date = "2026-11-11", dayMarketStart = "10:00:00.000", dayMarketEnd = "17:50:00.000" }
    };
    private sealed class DayHttpFixture(DayClock clock) : HttpMessageHandler
    {
        public int InitRequests, CalendarRequests, SearchRequests, PriceRequests;
        public bool Supported = true, FailPrices, Limited;
        public bool SawPrivateRequest, HadAuthorization;
        public decimal Price = 351.03m;
        public TaskCompletionSource? HoldPrices;
        public TaskCompletionSource PriceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HadAuthorization |= request.Headers.Authorization is not null;
            var uri = request.RequestUri!; object? result;
            if (uri.Host is not ("wts-api.tossinvest.com" or "wts-info-api.tossinvest.com")
                || !request.Headers.Contains("browser-tab-id")) throw new Exception("Unexpected day-market public request");
            if (uri.AbsolutePath == "/api/v3/init") { InitRequests++; result = new { }; }
            else if (uri.AbsolutePath == "/api/v2/system/trading-hours/integrated")
            { CalendarRequests++; result = new { us = DayCalendar() }; }
            else if (uri.AbsolutePath == "/api/v3/search-all/wts-auto-complete")
            {
                SearchRequests++;
                using var query = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var symbol = query.RootElement.GetProperty("query").GetString()!;
                if (symbol == "BAD") return new(HttpStatusCode.ServiceUnavailable);
                result = new[] { new { type = "PRODUCT", data = new { items = new[] {
                    new { symbol = "AAPU", market = "NSQ", productCode = "WRONGPRODUCT" },
                    new { symbol, market = "NSQ", productCode = "US19801212001" } } } } };
            }
            else if (uri.AbsolutePath == "/api/v2/stock-infos/US19801212001")
                result = new { code = "US19801212001", symbol = "AAPL", currency = "USD", daytimePriceSupported = Supported };
            else if (uri.AbsolutePath == "/api/v5/stock-prices")
            {
                PriceRequests++; PriceStarted.TrySetResult();
                if (HoldPrices is { } hold) await hold.Task.WaitAsync(cancellationToken);
                if (Limited)
                {
                    var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                    return limited;
                }
                if (FailPrices) return new(HttpStatusCode.ServiceUnavailable);
                result = new { prices = new[] { new { dt = clock.Now.AddSeconds(-1).ToString("O"), productCode = "US19801212001",
                    marketCurrency = "USD", session = "DAY_PRE_MAIN", close = Price, @base = 350.69m } } };
            }
            else { SawPrivateRequest = true; throw new Exception("Non-public day-market request"); }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result }), Encoding.UTF8, "application/json") };
        }
    }

    private static async Task DayMarketTests()
    {
        var clock = new DayClock(); var calendar = Json(JsonSerializer.Serialize(DayCalendar()));
        Check(UnitedStatesDayMarket.IsDayMarketOpen(calendar, clock.Now), "US daytime is open while the conventional US quote feed is closed");
        Check(!UnitedStatesDayMarket.IsDayMarketOpen(calendar, DateTimeOffset.Parse("2026-11-10T08:50:00Z")),
            "the provider's actual daytime closing boundary is exclusive");
        Check(!UnitedStatesDayMarket.IsDayMarketOpen(calendar, DateTimeOffset.Parse("2026-11-08T02:00:00Z")),
            "a weekend is not treated as a daytime trading day");
        var dst = Json(calendar.GetRawText().Replace("10:00:00.000", "09:00:00.000"));
        Check(UnitedStatesDayMarket.IsDayMarketOpen(dst, DateTimeOffset.Parse("2026-11-10T00:30:00Z")),
            "daytime opening follows the supplied DST-adjusted calendar instead of a fixed local schedule");
        var search = Json("[{\"data\":{\"items\":[{\"symbol\":\"AAPU\",\"market\":\"NSQ\",\"productCode\":\"WRONG\"},{\"symbol\":\"AAPL\",\"market\":\"NSQ\",\"productCode\":\"VERIFIED\"}]}}]");
        Check(UnitedStatesDayMarket.FindProduct(search, "aapl") == "VERIFIED", "the exact displayed ticker is verified instead of guessing an opaque product ID");
        Check(UnitedStatesDayMarket.FindProduct(Json("[{\"data\":{\"items\":[{\"symbol\":\"BRK.B\",\"market\":\"NYS\",\"productCode\":\"CLASSB\"}]}}]"), "BRK B") == "CLASSB",
            "class-share punctuation is matched without modifying the original provider code");
        var mapping = new Dictionary<string, string> { ["P"] = "US:AAPL.O" };
        var frame = Json("{\"prices\":[{\"productCode\":\"P\",\"marketCurrency\":\"USD\",\"session\":\"DAY_PRE_MAIN\",\"dt\":\"2026-11-10T02:29:59Z\",\"close\":351.03,\"base\":350.69}]}");
        var parsed = UnitedStatesDayMarket.ParsePrices(frame, mapping, clock.Now).Single();
        Check(parsed is { Code: "US:AAPL.O", Price: 351.03m, MarketStatus: "OPEN", Currency: "USD" }
            && Math.Abs(parsed.ChangePercent - .096951m) < .00001m, "daytime price, real trade timestamp and previous-close basis are preserved");
        Check(UnitedStatesDayMarket.ParsePrices(Json(frame.GetRawText().Replace("USD", "KRW")), mapping, clock.Now).Length == 0
            && UnitedStatesDayMarket.ParsePrices(Json(frame.GetRawText().Replace("351.03", "0")), mapping, clock.Now).Length == 0,
            "wrong currencies and zero daytime prices never replace a valid quote");
        Check(UnitedStatesDayMarket.ParsePrices(Json(frame.GetRawText().Replace("DAY_PRE_MAIN", "AFTER")), mapping, clock.Now).Length == 0,
            "the daytime supplement does not relabel after-hours data as daytime");
        var handler = new DayHttpFixture(clock);
        using (var day = new UnitedStatesDayMarket(handler, clock))
        {
            day.Register(new("US:AAPL.O", "애플", "NASDAQ", "AAPL"));
            day.Register(new("US:BAD", "bad", "NASDAQ", "BAD"));
            var first = await day.GetQuotesAsync(["US:AAPL.O", "US:BAD"], default);
            var again = await day.GetQuotesAsync(["US:AAPL.O"], default);
            Check(first.Quotes["US:AAPL.O"].Price == 351.03m && first.UnknownCodes.Contains("US:BAD"),
                "a bad symbol lookup does not discard the healthy daytime quote");
            Check(handler.InitRequests == 1 && handler.CalendarRequests == 1 && handler.SearchRequests == 2 && again.Quotes.Count == 1,
                "anonymous initialization, calendar and exact ticker mappings are reused across polls");
            Check(!handler.SawPrivateRequest && !handler.HadAuthorization, "daytime requests use no account, order or login API and no authorization header");
        }
        var unavailable = new DayHttpFixture(clock) { Supported = false };
        using (var day = new UnitedStatesDayMarket(unavailable, clock))
        {
            day.Register(new("US:AAPL.O", "애플", "NASDAQ", "AAPL"));
            var result = await day.GetQuotesAsync(["US:AAPL.O"], default);
            Check(result.OpenCodes.Count == 0 && result.UnknownCodes.Contains("US:AAPL.O") && unavailable.PriceRequests == 0,
                "missing daytime coverage is reported as unknown instead of inventing an open or fully closed market");
        }
        var limitedHandler = new DayHttpFixture(clock) { Limited = true };
        using (var day = new UnitedStatesDayMarket(limitedHandler, clock))
        {
            day.Register(new("US:AAPL.O", "애플", "NASDAQ", "AAPL"));
            await Throws<IOException>(async () => await day.GetQuotesAsync(["US:AAPL.O"], default), "daytime HTTP 429 is surfaced without a fabricated price");
            await Throws<IOException>(async () => await day.GetQuotesAsync(["US:AAPL.O"], default), "daytime Retry-After blocks repeated network requests");
            Check(limitedHandler.PriceRequests == 1, "only one daytime request reaches a rate-limited provider during cooldown");
        }
        var heldHandler = new DayHttpFixture(clock) { HoldPrices = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using (var day = new UnitedStatesDayMarket(heldHandler, clock))
        using (var cancellation = new CancellationTokenSource())
        {
            day.Register(new("US:AAPL.O", "애플", "NASDAQ", "AAPL"));
            var pending = day.GetQuotesAsync(["US:AAPL.O"], cancellation.Token);
            await heldHandler.PriceStarted.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
            await Throws<OperationCanceledException>(async () => await pending, "cancellation stops a pending daytime price request");
        }
        var primary = new PublicHttpFixture(); var supplement = new DayHttpFixture(clock);
        await using var service = new PublicMarketData(primary, supplement, clock);
        service.RegisterStocks([new("US:AAPL.O", "애플", "NASDAQ", "AAPL")]);
        var quote = await service.GetSnapshotAsync("US:AAPL.O");
        Check(quote is { Price: 351.03m, MarketStatus: "OPEN" } && quote.Source.Contains("데이마켓"),
            "a real daytime trade replaces a closed conventional feed in the shared service");
        Quote? delivered = null; ConnectionStatus? status = null;
        service.QuoteReceived += q => Volatile.Write(ref delivered, q);
        service.StatusChanged += s => Volatile.Write(ref status, s);
        await service.SetSubscriptionsAsync(["US:AAPL.O"]); service.Start();
        clock.Now = DateTimeOffset.Parse("2026-11-10T08:51:00Z");
        await Until(() => Volatile.Read(ref delivered)?.MarketStatus == "CLOSE");
        Check(delivered!.Price == 351.03m, "the full close retains the last daytime price instead of rolling back to the stale after-hours close");
        clock.Now = DateTimeOffset.Parse("2026-11-11T02:30:00Z"); supplement.Price = 352;
        await Until(() => Volatile.Read(ref delivered) is { Price: 352, MarketStatus: "OPEN" });
        Check(true, "the next daytime session resumes automatically on the existing subscription");
        supplement.FailPrices = true;
        await Until(() => Volatile.Read(ref delivered)?.MarketStatus == "UNKNOWN" && Volatile.Read(ref status)?.Connected == false);
        Check(delivered!.Price == 352, "a daytime feed failure retains its price and reports uncertainty instead of falsely declaring a full close");
        supplement.FailPrices = false; supplement.Price = 353; primary.RateLimited = true;
        await Until(() => Volatile.Read(ref delivered) is { Price: 353, MarketStatus: "OPEN" });
        Check(true, "daytime quotes continue when the conventional feed is rate-limited");
    }
}
