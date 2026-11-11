using System.Net;
using System.Text;
using System.Text.Json;

namespace StockViewer.Core;

internal sealed class TossRateLimitException(TimeSpan retryAfter) : IOException("토스 공개 시세 조회 제한")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>A fresh anonymous website visitor; never reads browser or account credentials.</summary>
internal sealed class TossPublicSession : IDisposable
{
    internal const string Api = "https://wts-api.tossinvest.com";
    internal const string Info = "https://wts-info-api.tossinvest.com";
    private readonly HttpClient http;
    private readonly CookieContainer cookies = new();
    private readonly string tabId = Guid.NewGuid().ToString();
    private readonly TimeProvider clock;
    private readonly string provider;
    private long blockedUntilTicks;
    private bool initialized;

    public TossPublicSession(HttpMessageHandler? handler, TimeProvider clock, string provider)
    {
        this.clock = clock;
        this.provider = provider;
        cookies.Add(new Cookie("deviceId", "WTS-" + Guid.NewGuid().ToString("N"), "/", ".tossinvest.com") { Secure = true });
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies }, disposeHandler: handler is null)
        { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StockViewer/2.0");
        http.DefaultRequestHeaders.Add("browser-tab-id", tabId);
        http.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
    }

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        if (initialized) return;
        await SendAsync(Api + "/api/v3/init?tabId=" + tabId, cancellation).ConfigureAwait(false);
        initialized = true;
    }

    public async Task<JsonElement> SendAsync(string url, CancellationToken cancellation, string? body = null)
    {
        using var trace = DiagnosticLog.Request(provider, url);
        try
        {
            var remaining = Interlocked.Read(ref blockedUntilTicks) - clock.GetUtcNow().UtcTicks;
            if (remaining > 0) { trace.RetryAfter(TimeSpan.FromTicks(remaining)); throw new TossRateLimitException(TimeSpan.FromTicks(remaining)); }
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (cookies.GetCookies(new Uri(Info))["XSRF-TOKEN"] is { } csrf) request.Headers.Add("X-XSRF-TOKEN", csrf.Value);
            }
            using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
            trace.Response(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || !response.IsSuccessStatusCode && response.Headers.RetryAfter is not null)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - clock.GetUtcNow() ?? TimeSpan.FromSeconds(30);
                if (delay <= TimeSpan.Zero) delay = TimeSpan.FromSeconds(30);
                trace.RetryAfter(delay);
                var until = (clock.GetUtcNow() + delay).UtcTicks;
                var previous = Interlocked.Read(ref blockedUntilTicks);
                while (previous < until)
                {
                    var observed = Interlocked.CompareExchange(ref blockedUntilTicks, until, previous);
                    if (observed == previous) break;
                    previous = observed;
                }
                throw new TossRateLimitException(delay);
            }
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest) initialized = false;
                throw new IOException($"토스 공개 시세 연결 실패 (HTTP {(int)response.StatusCode})");
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("result", out var result))
                throw new InvalidDataException("토스 공개 시세 응답 형식 오류");
            return result.Clone();
        }
        catch (Exception e) { trace.Fail(e, cancellation.IsCancellationRequested); throw; }
    }

    public void Dispose() => http.Dispose();
}
