using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace StockViewer.Core;

public sealed record TossDomesticProduct(string Code, string Symbol, bool NxtSupported, bool? KrxAfterSupported);
public sealed record TossDomesticBatch(IReadOnlyDictionary<string, Quote> Quotes, IReadOnlyDictionary<string, Exception> Errors);

/// <summary>Anonymous Toss domestic integrated prices, including NXT and KRX-only products.</summary>
public sealed class TossDomesticMarket : IDisposable
{
    private readonly TossPublicSession session;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, (TossDomesticProduct Product, DateTimeOffset Expires)> products = new();
    private JsonElement calendar;
    private DateTimeOffset calendarExpires;
    public TossDomesticMarket(HttpMessageHandler? handler = null, TimeProvider? clock = null)
    { this.clock = clock ?? TimeProvider.System; session = new(handler, this.clock, "toss.domestic"); }

    private static bool IsDataError(Exception e) => e is IOException or InvalidDataException or HttpRequestException or JsonException or OperationCanceledException;
    private async Task<TossDomesticProduct> ResolveAsync(string symbol, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        if (products.TryGetValue(symbol, out var saved) && now < saved.Expires) return saved.Product;
        var info = await session.SendAsync(TossPublicSession.Info + "/api/v2/stock-infos/code-or-symbol/" + Uri.EscapeDataString(symbol), cancellation).ConfigureAwait(false);
        if (info.ValueKind != JsonValueKind.Object) throw new InvalidDataException("토스 국내 종목 정보가 없습니다.");
        var code = KiwoomParser.Text(info, "code");
        if (KiwoomParser.Text(info, "symbol") != symbol || KiwoomParser.Text(info, "currency") != "KRW"
            || code.Length is < 1 or > 40 || !code.All(char.IsAsciiLetterOrDigit)
            || !info.TryGetProperty("market", out var market) || market.ValueKind != JsonValueKind.Object
            || KiwoomParser.Text(market, "code") is not ("KSP" or "KSQ" or "KNX"))
            throw new InvalidDataException("토스 국내 종목 식별자가 일치하지 않습니다.");
        var nxt = info.TryGetProperty("nxtSupported", out var support) && support.ValueKind == JsonValueKind.True;
        bool? after = null;
        try
        {
            var extra = await session.SendAsync(TossPublicSession.Info + "/api/v1/mds/info/krx-extended-hour-support-yn/" + code, cancellation).ConfigureAwait(false);
            if (extra.ValueKind == JsonValueKind.Object && KiwoomParser.Text(extra, "shortCode") == code
                && KiwoomParser.Text(extra, "date") == now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd")
                && extra.TryGetProperty("afterMarketPossYn", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
                after = flag.GetBoolean();
        }
        catch (Exception e) when (!cancellation.IsCancellationRequested && IsDataError(e))
        { DiagnosticLog.Write("metadata.unavailable", new { provider = "toss.domestic", code = symbol, operation = "krx_extended_hours", error = DiagnosticLog.Error(e) }, "warning"); }
        var product = new TossDomesticProduct(code, symbol, nxt, after);
        DiagnosticLog.Write("product.resolved", new { provider = "toss.domestic", code = symbol, product.NxtSupported, product.KrxAfterSupported });
        var midnight = new DateTimeOffset(now.ToOffset(TimeSpan.FromHours(9)).Date.AddDays(1), TimeSpan.FromHours(9));
        var expires = now.AddSeconds(after.HasValue ? 60 : 5);
        products[symbol] = (product, expires < midnight ? expires : midnight);
        return product;
    }

    public static string MarketStatus(JsonElement calendar, TossDomesticProduct product, DateTimeOffset now)
    {
        if (calendar.ValueKind != JsonValueKind.Object) return "UNKNOWN";
        var date = now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd");
        var days = new[] { "prevBizDay", "today", "nextBizDay" }.Select(key =>
            calendar.TryGetProperty(key, out var day) && day.ValueKind == JsonValueKind.Object ? day : default).ToArray();
        var current = days.FirstOrDefault(day => day.ValueKind == JsonValueKind.Object && KiwoomParser.Text(day, "date") == date);
        if (current.ValueKind != JsonValueKind.Object)
        {
            // A holiday lies between the provider's previous and next business dates.
            var dates = days.Where(d => d.ValueKind == JsonValueKind.Object).Select(d => KiwoomParser.Text(d, "date"))
                .Where(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)).Order().ToArray();
            return dates.Length >= 2 && string.CompareOrdinal(dates[0], date) < 0 && string.CompareOrdinal(date, dates[^1]) < 0 ? "CLOSE" : "UNKNOWN";
        }
        var unknown = false;
        bool Within(JsonElement hours, string startKey, string endKey, bool? supported = true)
        {
            if (supported == false) return false;
            if (!hours.TryGetProperty(startKey, out var a) || !hours.TryGetProperty(endKey, out var b)) { unknown = true; return false; }
            if (a.ValueKind == JsonValueKind.Null && b.ValueKind == JsonValueKind.Null) return false;
            if (!DateTimeOffset.TryParse(date + "T" + a + "+09:00", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !DateTimeOffset.TryParse(date + "T" + b + "+09:00", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                || end <= start) { unknown = true; return false; }
            if (now < start || now >= end) return false;
            if (!supported.HasValue) { unknown = true; return false; }
            return true;
        }
        if (!current.TryGetProperty("krxEntireTradingHours", out var krx) || krx.ValueKind != JsonValueKind.Object) unknown = true;
        else if (Within(krx, "startTime", "endTime") || Within(krx, "officialExtendedStartTime", "preLastPriceOrderEndTime")
            || Within(krx, "endTime", "afterExtraSinglePriceOrderStartTime")
            || Within(krx, "afterExtraSinglePriceOrderStartTime", "afterMarketEndTime", product.KrxAfterSupported)) return "OPEN";
        if (product.NxtSupported)
        {
            if (!current.TryGetProperty("nxtEntireTradingHours", out var nxt) || nxt.ValueKind != JsonValueKind.Object) unknown = true;
            else if (Within(nxt, "preMarketStartTime", "preMarketEndTime") || Within(nxt, "startTime", "endTime")
                || Within(nxt, "afterMarketSinglePriceEndTime", "afterMarketEndTime")
                || Within(nxt, "closingPriceStartTime", "closingPriceEndTime")) return "OPEN";
        }
        return unknown ? "UNKNOWN" : "CLOSE";
    }

    public static Quote[] ParsePrices(JsonElement frame, IReadOnlyDictionary<string, TossDomesticProduct> mapping,
        JsonElement calendar, DateTimeOffset receivedAt)
    {
        if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("prices", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("토스 국내 가격 응답 형식 오류");
        var result = new Dictionary<string, Quote>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !mapping.TryGetValue(KiwoomParser.Text(row, "productCode"), out var product)
                || KiwoomParser.Text(row, "marketCurrency") != "KRW") continue;
            var exchange = KiwoomParser.Text(row, "exchange");
            if (exchange is not ("integrated" or "krx" or "nxt") || exchange == "nxt" && !product.NxtSupported) continue;
            try
            {
                var price = KiwoomParser.Number(row, "close"); var basis = KiwoomParser.Number(row, "base");
                if (price <= 0 || basis <= 0 || !DateTimeOffset.TryParse(KiwoomParser.Text(row, "dt"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var sourceTime) || sourceTime > receivedAt.AddMinutes(1)) continue;
                var source = "토스증권 공개 시세 · " + (exchange == "integrated" ? "KRX+NXT 통합" : exchange.ToUpperInvariant());
                var quote = new Quote(product.Symbol, price, (price / basis - 1) * 100, receivedAt,
                    Source: source, SourceTime: sourceTime, MarketStatus: MarketStatus(calendar, product, receivedAt));
                if (!result.TryGetValue(quote.Code, out var previous) || previous.SourceTime <= quote.SourceTime) result[quote.Code] = quote;
            }
            catch (Exception e) when (e is InvalidDataException or OverflowException) { }
        }
        return result.Values.ToArray();
    }

    public async Task<TossDomesticBatch> GetQuotesAsync(IEnumerable<string> codes, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await session.InitializeAsync(cancellation).ConfigureAwait(false);
            var now = clock.GetUtcNow();
            if (now >= calendarExpires)
            {
                try
                {
                    var hours = await session.SendAsync(TossPublicSession.Api + "/api/v2/system/trading-hours/integrated", cancellation).ConfigureAwait(false);
                    if (hours.ValueKind != JsonValueKind.Object || !hours.TryGetProperty("ats", out var ats) || ats.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("국내 거래일 정보가 없습니다.");
                    calendar = ats; calendarExpires = now.AddSeconds(30);
                }
                catch (Exception e) when (!cancellation.IsCancellationRequested && IsDataError(e))
                {
                    DiagnosticLog.Write("calendar.unavailable", new { provider = "toss.domestic", error = DiagnosticLog.Error(e) }, "warning");
                    calendar = default; calendarExpires = now.AddSeconds(5);
                }
            }
            var symbols = codes.Distinct().ToArray();
            if (symbols.Any(code => !StockCode.IsDomestic(code))) throw new ArgumentException("잘못된 국내 종목코드입니다.");
            var mapping = new ConcurrentDictionary<string, TossDomesticProduct>(); var errors = new ConcurrentDictionary<string, Exception>();
            // Bound initial metadata work so a restored workspace does not spend
            // its entire first-price deadline waiting on serial product lookups.
            await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellation }, async (code, token) =>
            {
                try { var product = await ResolveAsync(code, token).ConfigureAwait(false); mapping[product.Code] = product; }
                catch (Exception e) when (!token.IsCancellationRequested && IsDataError(e)) { errors[code] = e; }
            }).ConfigureAwait(false);
            var quotes = new Dictionary<string, Quote>();
            foreach (var batch in mapping.Values.Chunk(20))
            {
                var frame = await session.SendAsync(TossPublicSession.Info + "/api/v5/stock-prices?productCodes="
                    + string.Join(',', batch.Select(p => p.Code)) + "&localizedCurrency=KRW", cancellation).ConfigureAwait(false);
                foreach (var quote in ParsePrices(frame, batch.ToDictionary(p => p.Code), calendar, clock.GetUtcNow())) quotes[quote.Code] = quote;
            }
            return new(quotes, errors);
        }
        finally { gate.Release(); }
    }
    public void Dispose() => session.Dispose();
}
