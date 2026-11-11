using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace StockViewer.Core;

public sealed record DayMarketBatch(IReadOnlyDictionary<string, Quote> Quotes, IReadOnlySet<string> OpenCodes, IReadOnlySet<string> UnknownCodes);

/// <summary>Read-only public Toss US daytime quotes using a fresh anonymous session.</summary>
public sealed class UnitedStatesDayMarket : IDisposable
{
    private const string Api = TossPublicSession.Api;
    private const string Info = TossPublicSession.Info;
    private readonly TossPublicSession session;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, string> symbols = new();
    private readonly Dictionary<string, Product> products = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private JsonElement calendar;
    private DateTimeOffset calendarExpires;
    private sealed record Product(string? Code, bool Supported, DateTimeOffset Expires);

    public UnitedStatesDayMarket(HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        session = new(handler, this.clock);
    }

    public void Register(Stock stock)
    {
        if (StockCode.IsUnitedStates(stock.Code) && !string.IsNullOrWhiteSpace(stock.Symbol))
            symbols[stock.Code] = stock.Symbol;
    }

    private Task<JsonElement> SendAsync(string url, CancellationToken cancellation, string? body = null)
        => session.SendAsync(url, cancellation, body);
    private Task InitializeAsync(CancellationToken cancellation) => session.InitializeAsync(cancellation);

    public static bool IsDayMarketOpen(JsonElement usCalendar, DateTimeOffset now)
    {
        if (usCalendar.ValueKind != JsonValueKind.Object) throw new InvalidDataException("데이마켓 거래일 형식 오류");
        var valid = false;
        foreach (var key in new[] { "prevBizDay", "today", "nextBizDay" })
        {
            if (!usCalendar.TryGetProperty(key, out var day) || day.ValueKind != JsonValueKind.Object) continue;
            if (!DateOnly.TryParseExact(KiwoomParser.Text(day, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
            valid = true;
            var date = KiwoomParser.Text(day, "date");
            if (DateTimeOffset.TryParse(date + "T" + KiwoomParser.Text(day, "dayMarketStart") + "+09:00", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                && DateTimeOffset.TryParse(date + "T" + KiwoomParser.Text(day, "dayMarketEnd") + "+09:00", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                && now >= start && now < end) return true;
        }
        if (!valid) throw new InvalidDataException("데이마켓 거래일 정보가 없습니다.");
        return false;
    }

    private static string NormalizeSymbol(string symbol) => symbol.Trim().Replace(' ', '.').ToUpperInvariant();
    public static string? FindProduct(JsonElement sections, string symbol)
    {
        if (sections.ValueKind != JsonValueKind.Array) throw new InvalidDataException("데이마켓 종목 검색 형식 오류");
        foreach (var section in sections.EnumerateArray())
        {
            if (section.ValueKind != JsonValueKind.Object || !section.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in items.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && NormalizeSymbol(KiwoomParser.Text(item, "symbol")) == NormalizeSymbol(symbol)
                    && KiwoomParser.Text(item, "market") is "NSQ" or "NYS" or "NYSE" or "AMX")
                {
                    var product = KiwoomParser.Text(item, "productCode");
                    if (product.Length is > 0 and <= 40 && product.All(char.IsAsciiLetterOrDigit)) return product;
                }
        }
        return null;
    }

    private async Task<Product> ResolveAsync(string symbol, CancellationToken cancellation)
    {
        if (products.TryGetValue(symbol, out var cached) && cached.Expires > clock.GetUtcNow()) return cached;
        var query = JsonSerializer.Serialize(new { query = symbol, sections = new[] { new { type = "PRODUCT" } } });
        var results = await SendAsync(Info + "/api/v3/search-all/wts-auto-complete", cancellation, query).ConfigureAwait(false);
        var productCode = FindProduct(results, symbol);
        var supported = false;
        if (productCode is not null)
        {
            var info = await SendAsync(Info + "/api/v2/stock-infos/" + productCode, cancellation).ConfigureAwait(false);
            if (info.ValueKind != JsonValueKind.Object || KiwoomParser.Text(info, "code") != productCode || NormalizeSymbol(KiwoomParser.Text(info, "symbol")) != NormalizeSymbol(symbol)
                || KiwoomParser.Text(info, "currency") != "USD") throw new InvalidDataException("데이마켓 종목 식별자가 일치하지 않습니다.");
            supported = info.TryGetProperty("daytimePriceSupported", out var daytime) && daytime.ValueKind == JsonValueKind.True;
        }
        return products[symbol] = new(productCode, supported, clock.GetUtcNow() + TimeSpan.FromMinutes(10));
    }

    public static Quote[] ParsePrices(JsonElement prices, IReadOnlyDictionary<string, string> productToCode, DateTimeOffset receivedAt)
    {
        if (prices.ValueKind != JsonValueKind.Object || !prices.TryGetProperty("prices", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("데이마켓 가격 형식 오류");
        var result = new List<Quote>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !productToCode.TryGetValue(KiwoomParser.Text(row, "productCode"), out var code)
                || KiwoomParser.Text(row, "marketCurrency") != "USD" || KiwoomParser.Text(row, "session") != "DAY_PRE_MAIN") continue;
            try
            {
                var price = KiwoomParser.Number(row, "close"); var basis = KiwoomParser.Number(row, "base");
                if (price <= 0 || basis <= 0 || !DateTimeOffset.TryParse(KiwoomParser.Text(row, "dt"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var sourceTime) || sourceTime > receivedAt.AddMinutes(1)) continue;
                result.Add(new(code, price, (price / basis - 1) * 100, receivedAt,
                    Source: "토스증권 공개 시세 · 미국 데이마켓", SourceTime: sourceTime, MarketStatus: "OPEN", Currency: "USD"));
            }
            catch (Exception e) when (e is InvalidDataException or OverflowException) { }
        }
        return result.ToArray();
    }

    public async Task<DayMarketBatch> GetQuotesAsync(IEnumerable<string> codes, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await InitializeAsync(cancellation).ConfigureAwait(false);
            if (calendar.ValueKind != JsonValueKind.Object || clock.GetUtcNow() >= calendarExpires)
            {
                var hours = await SendAsync(Api + "/api/v2/system/trading-hours/integrated", cancellation).ConfigureAwait(false);
                if (hours.ValueKind != JsonValueKind.Object || !hours.TryGetProperty("us", out calendar))
                    throw new InvalidDataException("데이마켓 거래일 정보가 없습니다.");
                IsDayMarketOpen(calendar, clock.GetUtcNow());
                calendarExpires = clock.GetUtcNow() + TimeSpan.FromMinutes(1);
            }
            var quotes = new Dictionary<string, Quote>(); var open = new HashSet<string>(); var unknown = new HashSet<string>();
            if (!IsDayMarketOpen(calendar, clock.GetUtcNow())) return new(quotes, open, unknown);
            var mapping = new Dictionary<string, string>();
            foreach (var code in codes.Distinct())
            {
                if (!symbols.TryGetValue(code, out var symbol)) { unknown.Add(code); continue; }
                try
                {
                    var product = await ResolveAsync(symbol, cancellation).ConfigureAwait(false);
                    if (product.Code is null) { unknown.Add(code); continue; }
                    if (!product.Supported) { unknown.Add(code); continue; }
                    open.Add(code); mapping[product.Code] = code;
                }
                catch (Exception e) when (!cancellation.IsCancellationRequested
                    && e is IOException or InvalidDataException or HttpRequestException or JsonException or OperationCanceledException)
                {
                    unknown.Add(code);
                    products[symbol] = new(null, false, clock.GetUtcNow() + TimeSpan.FromSeconds(5));
                }
            }
            if (mapping.Count > 0)
            {
                var frame = await SendAsync(Info + "/api/v5/stock-prices?productCodes=" + string.Join(',', mapping.Keys)
                    + "&localizedCurrency=KRW", cancellation).ConfigureAwait(false);
                foreach (var quote in ParsePrices(frame, mapping, clock.GetUtcNow())) quotes[quote.Code] = quote;
            }
            return new(quotes, open, unknown);
        }
        finally { gate.Release(); }
    }

    public void Dispose() => session.Dispose();
}
