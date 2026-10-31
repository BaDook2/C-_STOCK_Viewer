using System.Globalization;
using System.Net;
using System.Text.Json;

namespace StockViewer.Core;

/// <summary>Unauthenticated public quotes. One shared poller honors the provider's pollingInterval.</summary>
public sealed class PublicMarketData : IMarketDataService, IStockSearchService
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim quoteGate = new(1), wake = new(0, 1);
    private readonly Dictionary<string, Quote> cache = [];
    private readonly Dictionary<string, DateTimeOffset> nextFetch = [];
    private string[] subscriptions = [];
    private Task? runner;
    private int disposed;
    private long retryAfterTicks;
    public bool IsDemo => false;
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);
    public TimeSpan PollingInterval { get; private set; } = DefaultInterval;
    public event Action<Quote>? QuoteReceived;
    public event Action<ConnectionStatus>? StatusChanged;

    public PublicMarketData(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StockViewer/1.0");
    }
    private async Task<JsonElement> GetJsonAsync(string uri, CancellationToken cancellation)
    {
        var blockedUntil = Interlocked.Read(ref retryAfterTicks);
        if (DateTimeOffset.UtcNow.UtcTicks < blockedUntil)
            throw new PublicRateLimitException(TimeSpan.FromTicks(blockedUntil - DateTimeOffset.UtcNow.UtcTicks));
        using var response = await http.GetAsync(uri, cancellation);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(30);
            if (delay <= TimeSpan.Zero) delay = TimeSpan.FromSeconds(30);
            Interlocked.Exchange(ref retryAfterTicks, (DateTimeOffset.UtcNow + delay).UtcTicks);
            throw new PublicRateLimitException(delay);
        }
        if (!response.IsSuccessStatusCode) throw new IOException($"공개 시세 연결 실패 (HTTP {(int)response.StatusCode})");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("공개 데이터 응답이 올바르지 않습니다.");
        return document.RootElement.Clone();
    }
    public async Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
    {
        var all = new Dictionary<string, Stock>();
        foreach (var market in new[] { "KOSPI", "KOSDAQ" })
        {
            var root = await GetJsonAsync($"https://m.stock.naver.com/api/stocks/marketValue/{market}?page=1&pageSize=100", cancellation);
            if (!root.TryGetProperty("stocks", out var rows) || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("공개 종목 목록 형식을 확인할 수 없습니다.");
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var code = KiwoomParser.Text(row, "itemCode"); var name = KiwoomParser.Text(row, "stockName");
                if (StockCode.IsValid(code) && name.Length > 0) all[code] = new(code, name, market);
            }
        }
        if (all.Count == 0) throw new InvalidDataException("공개 종목 목록이 비어 있습니다.");
        return all.Values.ToArray();
    }
    public async Task<IReadOnlyList<Stock>> SearchAsync(string query, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var root = await GetJsonAsync("https://ac.stock.naver.com/ac?q=" + Uri.EscapeDataString(query.Trim()) + "&target=stock&st=111", cancellation);
        if (!root.TryGetProperty("items", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("공개 검색 응답 형식을 확인할 수 없습니다.");
        return rows.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object && KiwoomParser.Text(r, "nationCode") == "KOR")
            .Select(r => new Stock(KiwoomParser.Text(r, "code"), KiwoomParser.Text(r, "name"), KiwoomParser.Text(r, "typeCode")))
            .Where(s => StockCode.IsValid(s.Code) && s.Name.Length > 0).DistinctBy(s => s.Code).Take(12).ToArray();
    }
    public static TimeSpan ReadPollingInterval(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("공개 시세 응답이 올바르지 않습니다.");
        if (!root.TryGetProperty("pollingInterval", out var value)) return DefaultInterval;
        if (!double.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var ms)
            || !double.IsFinite(ms) || ms <= 0 || ms > TimeSpan.FromDays(1).TotalMilliseconds)
            throw new InvalidDataException("공개 시세 갱신 간격 형식을 확인할 수 없습니다.");
        return TimeSpan.FromMilliseconds(Math.Max(DefaultInterval.TotalMilliseconds, ms));
    }
    public static Quote[] ParseQuotes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("datas", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("공개 시세 응답 형식을 확인할 수 없습니다.");
        var result = new List<Quote>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            try
            {
                var code = KiwoomParser.Text(row, "itemCode");
                var price = KiwoomParser.Number(row, "closePrice");
                var percent = KiwoomParser.Number(row, "fluctuationsRatio");
                if (!StockCode.IsValid(code) || price <= 0) continue;
                if (row.TryGetProperty("compareToPreviousPrice", out var direction) && direction.ValueKind == JsonValueKind.Object)
                    percent = KiwoomParser.Text(direction, "code") switch
                    { "4" or "5" => -Math.Abs(percent), "1" or "2" => Math.Abs(percent), "3" => 0, _ => percent };
                DateTimeOffset? sourceTime = DateTimeOffset.TryParse(KiwoomParser.Text(row, "localTradedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
                var source = "네이버 공개 시세 · KRX";
                if (row.TryGetProperty("stockExchangeType", out var exchange) && exchange.ValueKind == JsonValueKind.Object && exchange.TryGetProperty("delayTime", out var delay)
                    && int.TryParse(delay.ToString(), out var minutes) && minutes > 0) source += $" · {minutes}분 지연";
                result.Add(new(code, price, percent, DateTimeOffset.UtcNow, sourceTime?.ToOffset(TimeSpan.FromHours(9)).ToString("HHmmss"),
                    Source: source, SourceTime: sourceTime, MarketStatus: KiwoomParser.Text(row, "marketStatus")));
            }
            catch (InvalidDataException) { /* A malformed row must not suppress other symbols. */ }
        }
        return result.ToArray();
    }
    private async Task RefreshLockedAsync(IEnumerable<string> codes, CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow;
        var due = codes.Where(c => !nextFetch.TryGetValue(c, out var next) || next <= now).Distinct().ToArray();
        foreach (var batch in due.Chunk(20))
        {
            var root = await GetJsonAsync("https://polling.finance.naver.com/api/realtime/domestic/stock/" + string.Join(',', batch), cancellation);
            var interval = ReadPollingInterval(root);
            var quotes = ParseQuotes(root).Where(q => batch.Contains(q.Code)).ToArray();
            PollingInterval = interval;
            foreach (var quote in quotes)
            {
                cache[quote.Code] = quote;
                nextFetch[quote.Code] = DateTimeOffset.UtcNow + interval;
                if (Volatile.Read(ref subscriptions).Contains(quote.Code)) QuoteReceived?.Invoke(quote);
            }
            var missing = batch.Except(quotes.Select(q => q.Code)).ToArray();
            if (missing.Length > 0)
            {
                foreach (var code in missing) nextFetch[code] = DateTimeOffset.UtcNow + interval;
                throw new InvalidDataException("일부 종목의 공개 시세가 제공되지 않습니다.");
            }
            StatusChanged?.Invoke(new($"공개 시세 연결 · {interval.TotalSeconds:0.#}초 간격 (제공처 기준)", true));
        }
    }
    public async Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
    {
        if (!StockCode.IsValid(code)) throw new ArgumentException("잘못된 종목코드입니다.", nameof(code));
        await quoteGate.WaitAsync(cancellation);
        try
        {
            await RefreshLockedAsync([code], cancellation);
            if (cache.TryGetValue(code, out var quote)) return quote;
            throw new InvalidDataException("해당 종목의 공개 시세가 없습니다.");
        }
        finally { quoteGate.Release(); }
    }
    public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var items = codes.Distinct().Order().ToArray();
        if (items.Any(c => !StockCode.IsValid(c))) throw new ArgumentException("잘못된 종목코드입니다.");
        if (items.Length > 200) throw new InvalidOperationException("최대 200종목까지 표시할 수 있습니다.");
        Volatile.Write(ref subscriptions, items);
        if (wake.CurrentCount == 0) { try { wake.Release(); } catch (SemaphoreFullException) { } }
        return Task.CompletedTask;
    }
    public void Start() => runner ??= RunAsync();
    private async Task RunAsync()
    {
        var failures = 0;
        while (!lifetime.IsCancellationRequested)
        {
            var delay = DefaultInterval;
            try
            {
                var codes = Volatile.Read(ref subscriptions);
                if (codes.Length == 0) { await wake.WaitAsync(lifetime.Token); continue; }
                await quoteGate.WaitAsync(lifetime.Token);
                try
                {
                    await RefreshLockedAsync(codes, lifetime.Token);
                    var times = Volatile.Read(ref subscriptions).Where(nextFetch.ContainsKey).Select(c => nextFetch[c]).ToArray();
                    if (times.Length > 0) delay = times.Min() - DateTimeOffset.UtcNow;
                    if (delay < TimeSpan.FromMilliseconds(50)) delay = TimeSpan.FromMilliseconds(50);
                }
                finally { quoteGate.Release(); }
                failures = 0;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(++failures, 5))));
                if (ex is PublicRateLimitException limited && limited.RetryAfter > delay) delay = limited.RetryAfter;
                StatusChanged?.Invoke(new("공개 시세 연결 끊김 · 마지막 가격 표시 · 재시도 중"));
            }
            try { await wake.WaitAsync(delay, lifetime.Token); }
            catch (OperationCanceledException) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); if (runner is not null) await runner;
        http.Dispose(); lifetime.Dispose();
    }
    private sealed class PublicRateLimitException(TimeSpan retryAfter) : IOException("공개 시세 조회 제한")
    { public TimeSpan RetryAfter { get; } = retryAfter; }
}
