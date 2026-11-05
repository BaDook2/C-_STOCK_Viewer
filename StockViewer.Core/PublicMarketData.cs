using System.Globalization;
using System.Net;
using System.Text.Json;

namespace StockViewer.Core;

/// <summary>Keyless one-second polling with independent markets and per-symbol recovery.</summary>
public sealed class PublicMarketData : IMarketDataService, IStockSearchService
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource lifetime = new();
    private readonly MarketState domestic = new(false), unitedStates = new(true);
    private readonly object lifecycleGate = new();
    private int disposed;
    private long retryAfterTicks;
    public bool IsDemo => false;
    private long providerIntervalTicks = TimeSpan.TicksPerSecond;
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);
    public TimeSpan PollingInterval => DefaultInterval;
    public TimeSpan ProviderPollingInterval => TimeSpan.FromTicks(Interlocked.Read(ref providerIntervalTicks));
    public event Action<Quote>? QuoteReceived;
    public event Action<ConnectionStatus>? StatusChanged;

    public PublicMarketData(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StockViewer/1.0");
        http.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
        http.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
    }
    private async Task<JsonElement> GetJsonAsync(string uri, CancellationToken cancellation)
    {
        var blockedUntil = Interlocked.Read(ref retryAfterTicks);
        if (DateTimeOffset.UtcNow.UtcTicks < blockedUntil)
            throw new PublicRateLimitException(TimeSpan.FromTicks(blockedUntil - DateTimeOffset.UtcNow.UtcTicks));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        using var response = await http.GetAsync(uri, linked.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests
            || !response.IsSuccessStatusCode && response.Headers.RetryAfter is not null)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(30);
            if (delay <= TimeSpan.Zero) delay = TimeSpan.FromSeconds(30);
            var until = (DateTimeOffset.UtcNow + delay).UtcTicks;
            var previous = Interlocked.Read(ref retryAfterTicks);
            while (previous < until)
            {
                var observed = Interlocked.CompareExchange(ref retryAfterTicks, until, previous);
                if (observed == previous) break;
                previous = observed;
            }
            throw new PublicRateLimitException(delay);
        }
        if (!response.IsSuccessStatusCode) throw new IOException($"공개 시세 연결 실패 (HTTP {(int)response.StatusCode})");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false));
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("공개 데이터 응답이 올바르지 않습니다.");
        return document.RootElement.Clone();
    }
    public async Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
    {
        var all = new Dictionary<string, Stock>();
        foreach (var market in new[] { "KOSPI", "KOSDAQ" })
        {
            var root = await GetJsonAsync($"https://m.stock.naver.com/api/stocks/marketValue/{market}?page=1&pageSize=100", cancellation).ConfigureAwait(false);
            if (!root.TryGetProperty("stocks", out var rows) || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("공개 종목 목록 형식을 확인할 수 없습니다.");
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var code = KiwoomParser.Text(row, "itemCode"); var name = KiwoomParser.Text(row, "stockName");
                if (StockCode.IsDomestic(code) && name.Length > 0) all[code] = new(code, name, market);
            }
        }
        if (all.Count == 0) throw new InvalidDataException("공개 종목 목록이 비어 있습니다.");
        return all.Values.ToArray();
    }
    public async Task<IReadOnlyList<Stock>> SearchAsync(string query, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var root = await GetJsonAsync("https://ac.stock.naver.com/ac?q=" + Uri.EscapeDataString(query.Trim()) + "&target=stock&st=111", cancellation).ConfigureAwait(false);
        if (!root.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("공개 검색 응답 형식을 확인할 수 없습니다.");
        var result = new List<Stock>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var name = KiwoomParser.Text(row, "name");
            var market = KiwoomParser.Text(row, "typeCode");
            var symbol = KiwoomParser.Text(row, "code");
            if (name.Length == 0 || symbol.Length == 0) continue;
            if (KiwoomParser.Text(row, "nationCode") == "KOR" && StockCode.IsDomestic(symbol))
                result.Add(new(symbol, name, market));
            else if (KiwoomParser.Text(row, "nationCode") == "USA")
            {
                // Provider IDs are opaque: AAPL.O, SPY and BRKb are all valid.
                // Never infer a suffix from the exchange or the displayed ticker.
                var code = "US:" + KiwoomParser.Text(row, "reutersCode");
                if (StockCode.IsUnitedStates(code)) result.Add(new(code, name, market, symbol));
            }
        }
        return result.DistinctBy(s => s.Code).Take(12).ToArray();
    }
    public static TimeSpan ReadPollingInterval(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("공개 시세 응답이 올바르지 않습니다.");
        if (!root.TryGetProperty("pollingInterval", out var value)) return DefaultInterval;
        if (!double.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var ms)
            || !double.IsFinite(ms) || ms <= 0 || ms > TimeSpan.FromDays(1).TotalMilliseconds)
            return DefaultInterval; // Metadata must not suppress otherwise valid prices.
        return TimeSpan.FromMilliseconds(Math.Max(DefaultInterval.TotalMilliseconds, ms));
    }
    private static decimal ApplyDirection(JsonElement row, decimal value)
    {
        if (!row.TryGetProperty("compareToPreviousPrice", out var direction) || direction.ValueKind != JsonValueKind.Object) return value;
        return KiwoomParser.Text(direction, "code") switch
        { "4" or "5" => -Math.Abs(value), "1" or "2" => Math.Abs(value), "3" => 0, _ => value };
    }
    private static DateTimeOffset? ReadSourceTime(JsonElement row) => DateTimeOffset.TryParse(
        KiwoomParser.Text(row, "localTradedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    public static Quote[] ParseQuotes(JsonElement root, bool unitedStates = false)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("datas", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("공개 시세 응답 형식을 확인할 수 없습니다.");
        var result = new List<Quote>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            try
            {
                var code = unitedStates ? "US:" + KiwoomParser.Text(row, "reutersCode") : KiwoomParser.Text(row, "itemCode");
                var price = KiwoomParser.Number(row, "closePrice");
                var percent = ApplyDirection(row, KiwoomParser.Number(row, "fluctuationsRatio"));
                if (!(unitedStates ? StockCode.IsUnitedStates(code) : StockCode.IsDomestic(code)) || price <= 0) continue;
                var currency = unitedStates ? "USD" : "KRW";
                if (row.TryGetProperty("currencyType", out var currencyInfo) && currencyInfo.ValueKind == JsonValueKind.Object
                    && KiwoomParser.Text(currencyInfo, "code") != currency) continue;
                var sourceTime = ReadSourceTime(row);
                var marketStatus = KiwoomParser.Text(row, "marketStatus");
                var source = "네이버 공개 시세 · " + (unitedStates ? "미국" : "KRX");
                if (row.TryGetProperty("stockExchangeType", out var exchange) && exchange.ValueKind == JsonValueKind.Object)
                {
                    if (unitedStates && KiwoomParser.Text(exchange, "name") is { Length: > 0 } name) source = "네이버 공개 시세 · " + name;
                    if (exchange.TryGetProperty("delayTime", out var delay) && int.TryParse(delay.ToString(), out var minutes) && minutes > 0)
                        source += $" · {minutes}분 지연";
                }
                if (unitedStates && sourceTime.HasValue && row.TryGetProperty("overMarketPriceInfo", out var extended)
                    && extended.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        var session = KiwoomParser.Text(extended, "tradingSessionType");
                        var extendedTime = ReadSourceTime(extended);
                        var extendedPrice = KiwoomParser.Number(extended, "overPrice");
                        if (session is "PRE_MARKET" or "AFTER_MARKET" && extendedTime > sourceTime && extendedPrice > 0)
                        {
                            // Premarket follows the previous session's regular close. After-hours
                            // follows today's close, so reconstruct the previous day's close.
                            // The extended feed's own percentage uses a different base.
                            var previousClose = session == "PRE_MARKET" ? price
                                : price - ApplyDirection(row, KiwoomParser.Number(row, "compareToPreviousClosePrice"));
                            if (previousClose > 0)
                            {
                                var extendedPercent = (extendedPrice / previousClose - 1) * 100;
                                price = extendedPrice; percent = extendedPercent;
                                sourceTime = extendedTime; marketStatus = KiwoomParser.Text(extended, "overMarketStatus");
                                source += session == "PRE_MARKET" ? " · 프리마켓" : " · 애프터마켓";
                            }
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or OverflowException) { /* Preserve valid regular data. */ }
                }
                result.Add(new(code, price, percent, DateTimeOffset.UtcNow, sourceTime?.ToOffset(TimeSpan.FromHours(9)).ToString("HHmmss"),
                    Source: source, SourceTime: sourceTime, MarketStatus: marketStatus, Currency: currency));
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException) { /* Preserve the other symbols. */ }
        }
        return result.ToArray();
    }
    private void Report(MarketState market, string code, ConnectionStatus status, bool replay = false)
    {
        var changed = !market.Statuses.TryGetValue(code, out var previous) || previous != status;
        market.Statuses[code] = status;
        if ((changed || replay) && Volatile.Read(ref market.Subscriptions).Contains(code)) StatusChanged?.Invoke(status);
    }
    private void Failed(MarketState market, string code, Exception error)
    {
        market.Errors[code] = error;
        var failures = market.Failures.TryGetValue(code, out var count) ? Math.Min(count + 1, 5) : 1;
        market.Failures[code] = failures;
        var delay = TimeSpan.FromSeconds(Math.Min(10, Math.Pow(2, failures - 1)));
        if (error is PublicRateLimitException limited && limited.RetryAfter > delay) delay = limited.RetryAfter;
        market.NextFetch[code] = DateTimeOffset.UtcNow + delay;
        Report(market, code, new($"시세 조회 지연 · 마지막 가격 유지 · {delay.TotalSeconds:0.#}초 후 재시도", Code: code));
    }
    private async Task RefreshLockedAsync(MarketState market, IEnumerable<string> codes, CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow;
        var due = codes.Where(c => !market.NextFetch.TryGetValue(c, out var next) || next <= now).Distinct().ToArray();
        foreach (var batch in due.Chunk(20))
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                var path = market.UnitedStates ? "worldstock" : "domestic";
                var providerCodes = batch.Select(c => Uri.EscapeDataString(StockCode.ProviderCode(c)));
                var root = await GetJsonAsync($"https://polling.finance.naver.com/api/realtime/{path}/stock/" + string.Join(',', providerCodes), cancellation).ConfigureAwait(false);
                Interlocked.Exchange(ref providerIntervalTicks, ReadPollingInterval(root).Ticks);
                var quotes = ParseQuotes(root, market.UnitedStates).Where(q => batch.Contains(q.Code)).ToArray();
                // Schedule from request start rather than adding another second after network latency.
                var next = startedAt + DefaultInterval;
                var earliest = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(50);
                if (next < earliest) next = earliest;
                foreach (var quote in quotes)
                {
                    market.Cache[quote.Code] = quote; market.NextFetch[quote.Code] = next;
                    market.Errors.Remove(quote.Code); market.Failures.Remove(quote.Code);
                    if (Volatile.Read(ref market.Subscriptions).Contains(quote.Code)) QuoteReceived?.Invoke(quote);
                    Report(market, quote.Code, new("공개 시세 연결 · 1초 목표", true, Code: quote.Code));
                }
                foreach (var code in batch.Except(quotes.Select(q => q.Code)))
                    Failed(market, code, new InvalidDataException("해당 종목의 공개 시세가 제공되지 않습니다."));
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested
                && (ex is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException))
            {
                foreach (var code in batch) Failed(market, code, ex);
            }
        }
    }
    public async Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
    {
        if (!StockCode.IsValid(code)) throw new ArgumentException("잘못된 종목코드입니다.", nameof(code));
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var market = StockCode.IsUnitedStates(code) ? unitedStates : domestic;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        await market.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await RefreshLockedAsync(market, [code], linked.Token).ConfigureAwait(false);
            if (market.Statuses.TryGetValue(code, out var status)) Report(market, code, status, replay: true);
            if (market.Cache.TryGetValue(code, out var quote)) return quote;
            if (market.Errors.TryGetValue(code, out var error)) throw error;
            throw new InvalidDataException("해당 종목의 공개 시세가 없습니다.");
        }
        finally { market.Gate.Release(); }
    }
    public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var items = codes.Distinct().Order().ToArray();
        if (items.Any(c => !StockCode.IsValid(c))) throw new ArgumentException("잘못된 종목코드입니다.");
        if (items.Length > 200) throw new InvalidOperationException("최대 200종목까지 표시할 수 있습니다.");
        foreach (var market in new[] { domestic, unitedStates })
        {
            Volatile.Write(ref market.Subscriptions, items.Where(c => StockCode.IsUnitedStates(c) == market.UnitedStates).ToArray());
            if (market.Wake.CurrentCount == 0) { try { market.Wake.Release(); } catch (SemaphoreFullException) { } }
        }
        return Task.CompletedTask;
    }
    public void Start()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            foreach (var market in new[] { domestic, unitedStates }) market.Runner ??= Task.Run(() => RunAsync(market));
        }
    }
    private async Task RunAsync(MarketState market)
    {
        while (!lifetime.IsCancellationRequested)
        {
            var delay = DefaultInterval;
            try
            {
                while (market.Wake.Wait(0)) { } // Consume notifications represented by this subscription snapshot.
                var codes = Volatile.Read(ref market.Subscriptions);
                if (codes.Length == 0) { await market.Wake.WaitAsync(lifetime.Token).ConfigureAwait(false); continue; }
                await market.Gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try
                {
                    await RefreshLockedAsync(market, codes, lifetime.Token).ConfigureAwait(false);
                    var current = Volatile.Read(ref market.Subscriptions);
                    if (current.Length > 0) delay = current.Select(c => market.NextFetch.TryGetValue(c, out var next) ? next : DateTimeOffset.UtcNow).Min() - DateTimeOffset.UtcNow;
                    if (delay < TimeSpan.FromMilliseconds(50)) delay = TimeSpan.FromMilliseconds(50);
                }
                finally { market.Gate.Release(); }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
            {
                delay = DefaultInterval;
                if (ex is PublicRateLimitException limited && limited.RetryAfter > delay) delay = limited.RetryAfter;
                foreach (var code in Volatile.Read(ref market.Subscriptions))
                    StatusChanged?.Invoke(new("시세 연결 지연 · 마지막 가격 유지 · 재시도 중", Code: code));
            }
            try { await market.Wake.WaitAsync(delay, lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        Task[] runners;
        lock (lifecycleGate) runners = new[] { domestic.Runner, unitedStates.Runner }.OfType<Task>().ToArray();
        await Task.WhenAll(runners).ConfigureAwait(false);
        http.Dispose(); lifetime.Dispose();
    }
    private sealed class MarketState(bool unitedStates)
    {
        public bool UnitedStates { get; } = unitedStates;
        public SemaphoreSlim Gate { get; } = new(1);
        public SemaphoreSlim Wake { get; } = new(0, 1);
        public Dictionary<string, Quote> Cache { get; } = [];
        public Dictionary<string, DateTimeOffset> NextFetch { get; } = [];
        public Dictionary<string, int> Failures { get; } = [];
        public Dictionary<string, Exception> Errors { get; } = [];
        public Dictionary<string, ConnectionStatus> Statuses { get; } = [];
        public string[] Subscriptions = [];
        public Task? Runner;
    }
    private sealed class PublicRateLimitException(TimeSpan retryAfter) : IOException("공개 시세 조회 제한")
    { public TimeSpan RetryAfter { get; } = retryAfter; }
}
