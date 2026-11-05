using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockViewer.Core;

public sealed record Stock(string Code, string Name, string Market, string? Symbol = null)
{
    [JsonIgnore] public string DisplayCode => Symbol ?? StockCode.ProviderCode(Code);
}
public static class StockCode
{
    // Namespace US identifiers so a six-letter ticker cannot collide with a Korean code.
    private const string UsPrefix = "US:";
    public static bool IsDomestic(string code) => code.Length == 6 && code.All(char.IsAsciiLetterOrDigit);
    public static bool IsUnitedStates(string code) => code.StartsWith(UsPrefix, StringComparison.Ordinal)
        && code.Length is > 3 and <= 27 && char.IsAsciiLetterOrDigit(code[3])
        && !code.AsSpan(3).ContainsAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-");
    public static bool IsValid(string code) => IsDomestic(code) || IsUnitedStates(code);
    public static string ProviderCode(string code) => code.StartsWith(UsPrefix, StringComparison.Ordinal) ? code[3..] : code;
}
public sealed record Quote(string Code, decimal Price, decimal ChangePercent, DateTimeOffset ReceivedAt,
    string? TradeTime = null, bool IsDemo = false, string Source = "KRX", DateTimeOffset? SourceTime = null, string? MarketStatus = null,
    string Currency = "KRW");
public sealed record ConnectionStatus(string Message, bool Connected = false, bool IsDemo = false, string? Code = null);
public sealed record ApiCredentials(string AppKey, string SecretKey, bool IsMock = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AppKey) || string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("App Key와 Secret Key를 모두 입력해 주세요.");
    }
    public override string ToString() => $"Kiwoom ({(IsMock ? "Mock" : "Live")}) [credentials hidden]";
}

public interface IMarketDataService : IAsyncDisposable
{
    event Action<Quote>? QuoteReceived;
    event Action<ConnectionStatus>? StatusChanged;
    bool IsDemo { get; }
    Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default);
    Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default);
    void Start();
    Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default);
}

public interface IStockSearchService
{
    Task<IReadOnlyList<Stock>> SearchAsync(string query, CancellationToken cancellation = default);
}

public static class StockSearch
{
    private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    public static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormC)
        .Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    private static string Chosung(string text) => new(text.Select(c => c is >= '가' and <= '힣'
        ? Initials[(c - '가') / 588] : c).ToArray());
    public static bool IsExactMatch(Stock stock, string query)
    {
        var q = Normalize(query);
        return Normalize(stock.Name) == q || Normalize(stock.Code) == q || Normalize(stock.DisplayCode) == q;
    }
    public static Stock[] Find(IEnumerable<Stock> stocks, string query, int limit = 12)
    {
        var q = Normalize(query);
        if (q.Length == 0) return [];
        return stocks.Select(s => (Stock: s, Name: Normalize(s.Name), Symbol: Normalize(s.DisplayCode), Code: Normalize(s.Code)))
            .Where(x => x.Name.Contains(q) || x.Symbol.Contains(q) || x.Code.Contains(q) || Chosung(x.Name).Contains(q))
            .OrderBy(x => x.Name == q || x.Symbol == q || x.Code == q ? 0 : x.Name.StartsWith(q) || x.Symbol.StartsWith(q) ? 1 : 2)
            .ThenBy(x => x.Stock.Name.Length).ThenBy(x => x.Stock.Code)
            .Take(limit).Select(x => x.Stock).ToArray();
    }
}

/// <summary>Provider signs on prices indicate direction; only the rate retains its sign.</summary>
public static class KiwoomParser
{
    public static string Text(JsonElement root, string key) => root.TryGetProperty(key, out var v) ? v.ToString().Trim() : "";
    public static decimal Number(JsonElement root, string key)
    {
        if (!decimal.TryParse(Text(root, key).Replace(",", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) throw new InvalidDataException("시세 응답의 숫자 형식을 확인할 수 없습니다.");
        return value;
    }
    public static Quote Snapshot(string code, JsonElement root)
    {
        var price = Math.Abs(Number(root, "cur_prc"));
        if (price <= 0) throw new InvalidDataException("현재가가 제공되지 않는 종목입니다.");
        return new(code, price, Number(root, "flu_rt"), DateTimeOffset.UtcNow);
    }
    public static IEnumerable<Quote> Realtime(JsonElement root)
    {
        if (Text(root, "trnm") != "REAL" || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) yield break;
        foreach (var row in data.EnumerateArray())
        {
            if (Text(row, "type") != "0B" || !row.TryGetProperty("values", out var v)) continue;
            Quote? quote = null;
            try
            {
                var price = Math.Abs(Number(v, "10"));
                var code = Text(row, "item");
                if (price > 0 && code.Length > 0)
                    quote = new(code, price, Number(v, "12"), DateTimeOffset.UtcNow, Text(v, "20"));
            }
            catch (InvalidDataException) { /* Keep valid rows in the same frame. */ }
            if (quote is not null) yield return quote;
        }
    }
}
