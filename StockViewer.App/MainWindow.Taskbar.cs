namespace StockViewer.Desktop;

public partial class MainWindow
{
    private QuoteWindow? taskbarPopup;
    internal QuoteWindow? TaskbarPopup => taskbarPopup;
    private void RefreshTaskbarPopup()
    {
        var representative = !closing && searchHidden
            ? popups.Values.FirstOrDefault(p => ReferenceEquals(p, taskbarPopup) && p.IsVisible)
                ?? popups.Values.FirstOrDefault(p => p.IsVisible)
            : null;
        foreach (var popup in popups.Values) popup.ShowInTaskbar = ReferenceEquals(popup, representative);
        taskbarPopup = representative;
        PopupTopmost.QueueCheck();
    }
    private void CloseFromTaskbar(object? sender, EventArgs e) => _ = EndSessionAsync();
}
