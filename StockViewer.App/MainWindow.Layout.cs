using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using StockViewer.Core;

namespace StockViewer.Desktop;

public partial class MainWindow
{
    private LayoutNoticeWindow? layoutNotice;
    private bool restoringLayout;
    public string LayoutMessage { get; private set; } = "";
    private static PopupState CapturePopup(QuoteWindow popup) => new(popup.Stock,
        popup.Position.X, popup.Position.Y, popup.IsFixed, popup.NameVisible);
    public bool SavePopupLayout()
    {
        if (closing || restoringLayout) return false;
        var saved = popups.Values.Where(p => p.IsVisible).Select(CapturePopup).ToArray();
        if (saved.Length == 0) { LayoutMessage = "저장할 가격창이 없습니다. 종목을 먼저 열어 주세요."; return false; }
        try
        {
            settings.SavePopupLayout(new(PopupLayout.CurrentVersion, saved));
            LayoutMessage = $"가격창 {saved.Length}개의 종목·위치·맨 위 고정 상태를 저장했습니다.";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { LayoutMessage = "가격창 배치를 저장하지 못했습니다. 저장 공간과 권한을 확인해 주세요."; return false; }
    }
    public async Task<bool> RestorePopupLayoutAsync()
    {
        if (closing || searchHidden || restoringLayout) return false;
        var layout = settings.LoadPopupLayout();
        if (layout is null) { LayoutMessage = "불러올 배치가 없습니다. 가격창 배치를 먼저 저장해 주세요."; return false; }
        restoringLayout = true; SaveLayoutButton.IsEnabled = LoadLayoutButton.IsEnabled = false;
        SearchBox.IsEnabled = SearchButton.IsEnabled = false;
        try
        {
            // Include symbols absent from today's search catalog, such as saved
            // US tickers. Register them before subscribing for the first quote.
            stocks = layout.Popups.Select(p => p.Stock).Concat(stocks).DistinctBy(s => s.Code).ToArray();
            if (service is PublicMarketData publicData) publicData.RegisterStocks(stocks);
            var opening = layout.Popups.Select(async state =>
            {
                if (popups.TryGetValue(state.Stock.Code, out var existing))
                { RestorePopup(existing, state); return existing; }
                return await OpenStockAtAsync(state.Stock, state);
            }).ToArray();
            var restored = await Task.WhenAll(opening);
            if (closing || searchHidden) return false;
            var count = restored.Count(p => p is { IsVisible: true });
            if (count == layout.Popups.Length)
            {
                var savedCodes = layout.Popups.Select(p => p.Stock.Code).ToHashSet();
                foreach (var extra in popups.Values.Where(p => !savedCodes.Contains(p.Stock.Code)).ToArray()) extra.Close();
                PopupPlacement.AvoidQuotes(this, popups.Values);
                LayoutMessage = $"가격창 {count}개의 위치와 맨 위 고정 상태를 불러왔습니다.";
                return true;
            }
            // A feed failure must not destroy the user's currently open layout.
            PopupPlacement.AvoidQuotes(this, popups.Values);
            LayoutMessage = $"가격창 {layout.Popups.Length}개 중 {count}개를 불러왔습니다.\n시세를 받지 못한 종목은 다시 불러와 주세요. 기존 가격창은 유지했습니다.";
            return false;
        }
        finally
        {
            restoringLayout = false;
            SaveLayoutButton.IsEnabled = LoadLayoutButton.IsEnabled = SearchBox.IsEnabled = SearchButton.IsEnabled = true;
        }
    }
    private static void RestorePopup(QuoteWindow popup, PopupState state)
    {
        popup.SetFixed(false); popup.SetNameVisible(state.ShowName);
        popup.Measure(Size.Infinity);
        var desired = new PixelPoint(state.X, state.Y);
        var screen = popup.Screens.ScreenFromPoint(desired) ?? popup.Screens.Primary;
        if (screen is not null)
        {
            var size = PixelSize.FromSize(new Size(Math.Max(popup.ClientSize.Width, popup.DesiredSize.Width), 32), screen.Scaling);
            desired = PopupPlacement.ClampToWorkArea(desired, size, screen.WorkingArea);
        }
        popup.Position = desired;
        popup.SetFixed(state.IsFixed);
    }
    private void ShowLayoutNotice()
    {
        if (closing || searchHidden) return;
        if (layoutNotice is null)
        {
            var notice = new LayoutNoticeWindow(); layoutNotice = notice;
            notice.Closed += (_, _) => { if (ReferenceEquals(layoutNotice, notice)) layoutNotice = null; };
            notice.SetMessage(LayoutMessage); notice.Show(this);
        }
        else { layoutNotice.SetMessage(LayoutMessage); layoutNotice.Activate(); }
    }
    private void SaveLayoutClicked(object? sender, RoutedEventArgs e)
    { SavePopupLayout(); ShowLayoutNotice(); }
    private async void LoadLayoutClicked(object? sender, RoutedEventArgs e)
    {
        try { await RestorePopupLayoutAsync(); }
        catch (Exception error) when (IsDataError(error))
        { LayoutMessage = "가격창 배치를 불러오지 못했습니다. 네트워크 상태를 확인하고 다시 시도해 주세요."; }
        ShowLayoutNotice();
    }
}
