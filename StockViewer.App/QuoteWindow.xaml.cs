using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using StockViewer.Core;

namespace StockViewer.App;

public partial class QuoteWindow : Window
{
    public Stock Stock { get; }
    public bool IsFixed { get; private set; }
    public bool NameVisible => StockName.Visibility == Visibility.Visible;
    public Quote? LastQuote { get; private set; }
    public string DisplayedPrice => PriceText.Text;
    public string StatusMessage { get; private set; } = "시세 연결 대기";
    private HwndSource? source;
    private bool rightButtonDown, closeOnRightButtonUp, closeQueued, closed;
    private ConnectionStatus status = new("시세 연결 대기");
    public QuoteWindow(Stock stock, bool showName)
    {
        InitializeComponent(); Stock = stock;
        StockName.Text = stock.Name; StockName.ToolTip = stock.Name;
        SetNameVisible(showName);
        SourceInitialized += (_, _) => { source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source?.AddHook(WindowMessage); };
        Closed += (_, _) =>
        {
            closed = true;
            if (Mouse.Captured == this) Mouse.Capture(null);
            source?.RemoveHook(WindowMessage);
        };
    }
    public void SetNameVisible(bool visible)
    {
        StockName.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Title = visible ? Stock.Name + " · STOCK MINI" : "STOCK MINI";
    }
    public void SetFixed(bool enabled)
    {
        IsFixed = enabled; Topmost = enabled;
        SetStatus(StatusMessage);
    }
    public void ApplyQuote(Quote quote)
    {
        if (quote.Code != Stock.Code || LastQuote is { TradeTime: not null } && quote.TradeTime is null && !quote.IsDemo) return;
        if (LastQuote?.SourceTime is { } last && quote.SourceTime is { } current && current < last) return;
        LastQuote = quote;
        PriceText.Text = quote.Price.ToString("N0", CultureInfo.GetCultureInfo("ko-KR")) + "원";
        var sign = quote.ChangePercent > 0 ? "+" : "";
        PercentText.Text = $"{sign}{quote.ChangePercent:0.00}%";
        var brush = (Brush)FindResource(quote.ChangePercent > 0 ? "RedBrush" : quote.ChangePercent < 0 ? "BlueBrush" : "TextSecondaryBrush");
        PriceText.Foreground = brush; PercentText.Foreground = brush;
        RefreshStatus();
    }
    public void SetConnection(ConnectionStatus connection) { status = connection; RefreshStatus(); }
    public void ShowError(string message)
    { SetStatus(message); Card.Opacity = LastQuote is null ? 1 : .55; }
    public void ResetQuote()
    { LastQuote = null; PriceText.Text = "—"; PercentText.Text = "—"; PriceText.Foreground = PercentText.Foreground = (Brush)FindResource("TextMutedBrush"); RefreshStatus(); }
    private void SetStatus(string message)
    { StatusMessage = message; Card.ToolTip = message + $"\n{(IsFixed ? "Fix" : "Free")} · 우클릭: 고정 전환 · 우클릭 두 번: 닫기"; }
    private void RefreshStatus()
    {
        Card.Opacity = status.Connected || LastQuote is null ? 1 : .55;
        if (LastQuote is { IsDemo: true }) { SetStatus("DEMO · 예시 가격 · 실제 시세 아님"); return; }
        if (!status.Connected) { SetStatus(LastQuote is null ? status.Message : "연결 끊김 · 마지막 수신 가격"); return; }
        if (LastQuote is null) { SetStatus("시세를 불러오고 있어요…"); return; }
        if (LastQuote.SourceTime is { } sourceTime)
        {
            SetStatus(LastQuote.Source + (LastQuote.MarketStatus == "CLOSE" ? " · 장 마감" : "")
                + " · 기준 " + sourceTime.ToOffset(TimeSpan.FromHours(9)).ToString("MM.dd HH:mm:ss"));
            return;
        }
        var time = LastQuote.TradeTime;
        SetStatus(time is { Length: 6 } ? $"KRX · 최근 체결 {time[..2]}:{time[2..4]}:{time[4..]}"
            : "KRX · 최근 조회 " + LastQuote.ReceivedAt.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss"));
    }
    public void ProcessRightClick(int clickCount)
    {
        if (closed || closeQueued || !IsVisible || clickCount < 1) return;
        rightButtonDown = true;
        if (clickCount >= 2) closeOnRightButtonUp = true;
        else SetFixed(!IsFixed);
        // Keep the release directed at this HWND even if the pointer moves out.
        Mouse.Capture(this, CaptureMode.SubTree);
    }
    private void PopupRightClick(object sender, MouseButtonEventArgs e)
    { e.Handled = true; ProcessRightClick(e.ClickCount); }
    private void PopupRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!rightButtonDown || closed) return;
        rightButtonDown = false;
        if (closeOnRightButtonUp)
        {
            closeOnRightButtonUp = false; closeQueued = true;
            // Finish WM_RBUTTONUP (and its possible WM_CONTEXTMENU) before
            // destroying the window. Closing on down leaks up to the desktop.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (closed) return;
                if (Mouse.Captured == this) Mouse.Capture(null);
                Close();
            }));
        }
        else if (Mouse.Captured == this) Mouse.Capture(null);
    }
    private void PopupKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { e.Handled = true; if (rightButtonDown) closeOnRightButtonUp = true; else Close(); } }
    private void DragCard(object sender, MouseButtonEventArgs e)
    { if (!IsFixed && e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x007B) // WM_CONTEXTMENU: this popup has no context menu.
        { handled = true; return IntPtr.Zero; }
        if (IsFixed && msg == 0x0046 && lParam != IntPtr.Zero) // WM_WINDOWPOSCHANGING: also block native keyboard/system moves.
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            position.Flags |= 0x0002; // SWP_NOMOVE; keep z-order changes enabled.
            Marshal.StructureToPtr(position, lParam, false);
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
    { public IntPtr Hwnd, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
}
