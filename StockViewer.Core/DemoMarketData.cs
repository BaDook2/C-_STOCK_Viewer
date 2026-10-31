namespace StockViewer.Core;

/// <summary>Explicit, opt-in sample data. Never used as a live-feed fallback.</summary>
public sealed class DemoMarketData : IMarketDataService
{
    public static readonly Stock[] SampleStocks =
    [
        new("005930", "삼성전자", "KOSPI"), new("000660", "SK하이닉스", "KOSPI"),
        new("005935", "삼성전자우", "KOSPI"), new("373220", "LG에너지솔루션", "KOSPI"),
        new("207940", "삼성바이오로직스", "KOSPI"), new("005380", "현대차", "KOSPI"),
        new("000270", "기아", "KOSPI"), new("035420", "NAVER", "KOSPI"),
        new("035720", "카카오", "KOSPI"), new("068270", "셀트리온", "KOSPI"),
        new("012450", "한화에어로스페이스", "KOSPI"), new("105560", "KB금융", "KOSPI"),
        new("055550", "신한지주", "KOSPI"), new("028260", "삼성물산", "KOSPI"),
        new("006400", "삼성SDI", "KOSPI"), new("051910", "LG화학", "KOSPI"),
        new("086520", "에코프로", "KOSDAQ"), new("247540", "에코프로비엠", "KOSDAQ"),
        new("196170", "알테오젠", "KOSDAQ"), new("041510", "에스엠", "KOSDAQ"),
        new("042700", "한미반도체", "KOSPI"), new("009540", "HD한국조선해양", "KOSPI"),
        new("069500", "KODEX 200", "ETF"), new("102110", "TIGER 200", "ETF"),
        new("360750", "TIGER 미국S&P500", "ETF"), new("133690", "TIGER 미국나스닥100", "ETF")
    ];
    private readonly CancellationTokenSource lifetime = new();
    private string[] subscriptions = [];
    private Task? pump;
    private int disposed;
    private int step;
    public bool IsDemo => true;
    public event Action<Quote>? QuoteReceived;
    public event Action<ConnectionStatus>? StatusChanged;
    public Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default) => Task.FromResult<IReadOnlyList<Stock>>(SampleStocks);
    public Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
    {
        var index = Array.FindIndex(SampleStocks, s => s.Code == code);
        if (index < 0) throw new InvalidOperationException("데모 목록에서 종목을 선택해 주세요.");
        decimal baseline = index switch { 0 => 72500, 1 => 185500, 2 => 58900, _ => 30000 + index * 2350 };
        var percent = (index % 3 == 1 ? -1 : 1) * (1.25m + index * .17m) + (step % 7 - 3) * .02m;
        return Task.FromResult(new Quote(code, baseline + (step % 7 - 3) * 100, percent, DateTimeOffset.UtcNow, IsDemo: true));
    }
    public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default)
    { subscriptions = codes.Distinct().ToArray(); return Task.CompletedTask; }
    public void Start()
    {
        StatusChanged?.Invoke(new("데모 모드 · 예시 가격", true, true));
        pump ??= RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                step++;
                foreach (var code in subscriptions) QuoteReceived?.Invoke(await GetSnapshotAsync(code));
            }
        }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    { if (Interlocked.Exchange(ref disposed, 1) != 0) return; lifetime.Cancel(); if (pump is not null) await pump; lifetime.Dispose(); }
}
