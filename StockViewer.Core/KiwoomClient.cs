using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace StockViewer.Core;

public interface IQuoteSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellation);
    Task SendAsync(string json, CancellationToken cancellation);
    Task<string> ReceiveAsync(CancellationToken cancellation);
}

public sealed class QuoteSocket : IQuoteSocket
{
    private readonly ClientWebSocket socket = new();
    public Task ConnectAsync(Uri uri, CancellationToken cancellation) => socket.ConnectAsync(uri, cancellation);
    public Task SendAsync(string json, CancellationToken cancellation) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, cancellation);
    public async Task<string> ReceiveAsync(CancellationToken cancellation)
    {
        using var message = new MemoryStream();
        var buffer = new byte[16384];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellation);
            if (result.MessageType == WebSocketMessageType.Close) throw new IOException("실시간 연결 종료");
            message.Write(buffer, 0, result.Count);
            if (message.Length > 2_000_000) throw new InvalidDataException("실시간 응답 크기 초과");
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(message.ToArray());
    }
    public ValueTask DisposeAsync() { socket.Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>Read-only Kiwoom REST + one shared 0B WebSocket. Contains no order endpoints.</summary>
public sealed class KiwoomClient : IMarketDataService
{
    private readonly ApiCredentials credentials;
    private readonly HttpClient http;
    private readonly Func<IQuoteSocket> socketFactory;
    private readonly TimeSpan requestInterval;
    private readonly SemaphoreSlim httpGate = new(1), authGate = new(1), sendGate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private string? token;
    private DateTimeOffset expires, lastRequest;
    private string[] desired = [], registered = [];
    private IQuoteSocket? socket;
    private bool ready;
    private Task? runner;
    private CancellationTokenSource? activeSession;
    private int disposed;
    public bool IsDemo => false;
    public event Action<Quote>? QuoteReceived;
    public event Action<ConnectionStatus>? StatusChanged;

    public KiwoomClient(ApiCredentials credentials, HttpMessageHandler? handler = null,
        Func<IQuoteSocket>? socketFactory = null, TimeSpan? requestInterval = null)
    {
        credentials.Validate();
        this.credentials = credentials;
        this.socketFactory = socketFactory ?? (() => new QuoteSocket());
        this.requestInterval = requestInterval ?? TimeSpan.FromMilliseconds(credentials.IsMock ? 1100 : 650);
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new(credentials.IsMock ? "https://mockapi.kiwoom.com" : "https://api.kiwoom.com"), Timeout = TimeSpan.FromSeconds(18) };
    }
    public async Task AuthenticateAsync(CancellationToken cancellation = default) => _ = await GetTokenAsync(cancellation);
    private async Task<string> GetTokenAsync(CancellationToken cancellation)
    {
        await authGate.WaitAsync(cancellation);
        try
        {
            if (token is not null && expires > DateTimeOffset.UtcNow.AddMinutes(3)) return token;
            using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth2/token")
            { Content = JsonContent.Create(new { grant_type = "client_credentials", appkey = credentials.AppKey, secretkey = credentials.SecretKey }) };
            request.Headers.Add("api-id", "au10001");
            using var response = await SendHttpAsync(request, cancellation);
            using var doc = await ReadResponseAsync(response, cancellation);
            var value = KiwoomParser.Text(doc.RootElement, "token");
            if (value.Length == 0 || !DateTime.TryParseExact(KiwoomParser.Text(doc.RootElement, "expires_dt"), "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidDataException("키움 인증 응답을 확인할 수 없습니다.");
            expires = new DateTimeOffset(date, TimeSpan.FromHours(9));
            if (expires <= DateTimeOffset.UtcNow.AddMinutes(3)) throw new InvalidDataException("키움 토큰의 유효기간을 확인해 주세요.");
            return token = value;
        }
        finally { authGate.Release(); }
    }
    private async Task<HttpResponseMessage> SendHttpAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        await httpGate.WaitAsync(cancellation);
        try
        {
            var wait = requestInterval - (DateTimeOffset.UtcNow - lastRequest);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellation);
            lastRequest = DateTimeOffset.UtcNow;
            return await http.SendAsync(request, cancellation);
        }
        finally { httpGate.Release(); }
    }
    private static void Check(JsonElement root)
    {
        var code = KiwoomParser.Text(root, "return_code");
        if (code != "0")
        {
            var safeCode = int.TryParse(code, out var n) ? n.ToString(CultureInfo.InvariantCulture) : "형식 오류";
            throw new InvalidOperationException($"키움 요청이 거부되었습니다 ({safeCode}). 키·등록 IP·API 사용 권한을 확인해 주세요.");
        }
    }
    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"키움 연결 실패 (HTTP {(int)response.StatusCode}). 키·등록 IP·네트워크를 확인해 주세요.");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        try { Check(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private async Task<(JsonElement Root, string Next)> QueryAsync(string api, object body, string next, CancellationToken cancellation)
    {
        var access = await GetTokenAsync(cancellation);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/dostk/stkinfo") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("api-id", api);
        request.Headers.Add("cont-yn", next.Length == 0 ? "N" : "Y");
        request.Headers.Add("next-key", next);
        using var response = await SendHttpAsync(request, cancellation);
        using var doc = await ReadResponseAsync(response, cancellation);
        var more = response.Headers.TryGetValues("cont-yn", out var flags) && flags.FirstOrDefault() == "Y";
        var key = more && response.Headers.TryGetValues("next-key", out var keys) ? keys.FirstOrDefault() ?? "" : "";
        if (more && key.Length == 0) throw new InvalidDataException("종목 목록의 연속 조회 키가 누락되었습니다.");
        return (doc.RootElement.Clone(), key);
    }
    public async Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
    {
        var all = new Dictionary<string, Stock>();
        foreach (var (market, label) in new[] { ("0", "KOSPI"), ("10", "KOSDAQ"), ("8", "ETF"), ("50", "KONEX") })
        {
            var next = "";
            var seen = new HashSet<string>();
            for (var page = 0; ; page++)
            {
                if (page >= 100) throw new InvalidDataException("종목 목록 조회 한도를 초과했습니다.");
                var result = await QueryAsync("ka10099", new { mrkt_tp = market }, next, cancellation);
                if (!result.Root.TryGetProperty("list", out var rows) || rows.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("종목 목록 응답 형식을 확인할 수 없습니다.");
                foreach (var row in rows.EnumerateArray())
                {
                    var code = KiwoomParser.Text(row, "code"); var name = KiwoomParser.Text(row, "name");
                    if (StockCode.IsValid(code) && name.Length > 0) all[code] = new(code, name, label);
                }
                next = result.Next;
                if (next.Length == 0) break;
                if (!seen.Add(next)) throw new InvalidDataException("종목 목록의 연속 조회 키가 반복되었습니다.");
            }
        }
        if (all.Count == 0) throw new InvalidDataException("검색할 수 있는 종목 목록을 받지 못했습니다.");
        return all.Values.ToArray();
    }
    public async Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
    {
        if (!StockCode.IsValid(code)) throw new ArgumentException("잘못된 종목코드입니다.", nameof(code));
        var result = await QueryAsync("ka10001", new { stk_cd = code }, "", cancellation);
        return KiwoomParser.Snapshot(code, result.Root);
    }
    public void Start() => runner ??= RunAsync();
    public async Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default)
    {
        var values = codes.Distinct().Order().ToArray();
        if (values.Length > 200) throw new InvalidOperationException("동시에 최대 200개 종목까지 표시할 수 있습니다.");
        if (values.Any(c => !StockCode.IsValid(c))) throw new ArgumentException("잘못된 종목코드입니다.");
        desired = values;
        await SyncSubscriptionsAsync(cancellation);
    }
    private async Task SendAsync(object message, CancellationToken cancellation)
    {
        await sendGate.WaitAsync(cancellation);
        try { if (socket is not null) await socket.SendAsync(JsonSerializer.Serialize(message), cancellation); }
        finally { sendGate.Release(); }
    }
    private async Task SyncSubscriptionsAsync(CancellationToken cancellation)
    {
        await sendGate.WaitAsync(cancellation);
        try
        {
            if (!ready || socket is null) return;
            var items = desired;
            var remove = items.Length == 0;
            if (remove && registered.Length == 0) return;
            var payload = new { trnm = remove ? "REMOVE" : "REG", grp_no = "1", refresh = "0",
                data = new[] { new { item = remove ? registered : items, type = new[] { "0B" } } } };
            await socket.SendAsync(JsonSerializer.Serialize(payload), cancellation);
            registered = items;
        }
        catch (Exception e) when (e is WebSocketException or IOException or InvalidOperationException)
        {
            try { activeSession?.Cancel(); } catch (ObjectDisposedException) { }
            throw;
        }
        finally { sendGate.Release(); }
    }
    private async Task<string> ReceiveWithTimeoutAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(55));
        return await socket!.ReceiveAsync(timeout.Token);
    }
    private async Task RunAsync()
    {
        var attempt = 0;
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                StatusChanged?.Invoke(new("키움 실시간 연결 중…"));
                var access = await GetTokenAsync(lifetime.Token);
                using var session = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                activeSession = session;
                session.CancelAfter(expires - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2));
                await using var connection = socketFactory();
                socket = connection;
                registered = [];
                await connection.ConnectAsync(new Uri(credentials.IsMock
                    ? "wss://mockapi.kiwoom.com:10000/api/dostk/websocket"
                    : "wss://api.kiwoom.com:10000/api/dostk/websocket"), session.Token);
                await SendAsync(new { trnm = "LOGIN", token = access }, session.Token);
                // Registration starts only after LOGIN acknowledgement; PING can precede it.
                using (var loginTimeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
                {
                    loginTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                    while (true)
                    {
                        var json = await connection.ReceiveAsync(loginTimeout.Token);
                        using var doc = JsonDocument.Parse(json);
                        var kind = KiwoomParser.Text(doc.RootElement, "trnm");
                        if (kind == "PING") { await SendAsync(doc.RootElement, session.Token); continue; }
                        if (kind != "LOGIN") continue;
                        Check(doc.RootElement); break;
                    }
                }
                ready = true;
                StatusChanged?.Invoke(new(credentials.IsMock ? "키움 모의서버 연결 · 체결 대기" : "키움 연결 · 체결 대기", true));
                await SyncSubscriptionsAsync(session.Token);
                while (!session.IsCancellationRequested)
                {
                    var json = await ReceiveWithTimeoutAsync(session.Token);
                    using var doc = JsonDocument.Parse(json);
                    switch (KiwoomParser.Text(doc.RootElement, "trnm"))
                    {
                        case "PING": await SendAsync(doc.RootElement, session.Token); break;
                        case "REG":
                        case "REMOVE":
                            Check(doc.RootElement); attempt = 0;
                            StatusChanged?.Invoke(new(credentials.IsMock ? "키움 모의서버 · 실시간 수신 중" : "키움 연결 · 실시간 수신 중", true)); break;
                        case "REAL":
                            foreach (var quote in KiwoomParser.Realtime(doc.RootElement))
                                if (desired.Contains(quote.Code)) QuoteReceived?.Invoke(quote);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or OperationCanceledException or InvalidOperationException or JsonException)
            {
                StatusChanged?.Invoke(new("연결 끊김 · 마지막 가격 표시 · 재연결 중…"));
            }
            finally
            {
                await sendGate.WaitAsync();
                try { ready = false; socket = null; activeSession = null; }
                finally { sendGate.Release(); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(++attempt, 5)))), lifetime.Token); }
            catch (OperationCanceledException) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        if (runner is not null) await runner;
        http.Dispose(); lifetime.Dispose();
    }
}
