namespace StockViewer.Desktop;

public partial class MainWindow
{
    private QuoteWindow? taskbarPopup;
    internal QuoteWindow? TaskbarPopup => taskbarPopup;
    private void RefreshTaskbarPopup(QuoteWindow? leaving = null)
    {
        var representative = !closing && searchHidden
            ? popups.Values.FirstOrDefault(p => ReferenceEquals(p, taskbarPopup) && p.IsVisible && p != leaving)
                ?? popups.Values.FirstOrDefault(p => p.IsVisible && p != leaving)
            : null;
        // Register the successor before removing the previous taskbar entry.
        // Otherwise the shell briefly sees an application without any windows.
        if (representative is not null) representative.ShowInTaskbar = true;
        foreach (var popup in popups.Values)
            if (!ReferenceEquals(popup, representative)) popup.ShowInTaskbar = false;
        taskbarPopup = representative;
        PopupTopmost.QueueCheck();
    }
    private void PopupLeavingTaskbar(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (!closing && searchHidden && sender is QuoteWindow popup && ReferenceEquals(popup, taskbarPopup))
            RefreshTaskbarPopup(popup);
    }
    private void CloseFromTaskbar(object? sender, EventArgs e) => _ = EndSessionAsync();
}
