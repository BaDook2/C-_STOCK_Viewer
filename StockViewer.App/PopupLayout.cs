using StockViewer.Core;

namespace StockViewer.Desktop;

public sealed record PopupState(Stock Stock, int X, int Y, bool IsFixed, bool ShowName);
public sealed record PopupLayout(int Version, PopupState[] Popups)
{
    public const int CurrentVersion = 1;
    public bool IsValid => Version == CurrentVersion && Popups is { Length: > 0 and <= 200 }
        && Popups.All(p => p is not null && p.Stock is { Code: not null } stock && StockCode.IsValid(stock.Code)
            && !string.IsNullOrWhiteSpace(stock.Name) && stock.Name.Length <= 256 && !string.IsNullOrWhiteSpace(stock.Market)
            && Math.Abs((long)p.X) <= 1_000_000 && Math.Abs((long)p.Y) <= 1_000_000)
        && Popups.Select(p => p.Stock.Code).Distinct(StringComparer.Ordinal).Count() == Popups.Length;
}
