using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StockViewer.Core;

namespace StockViewer.Desktop;

public partial class QuoteWindow : Window
{
    public Stock Stock { get; }
    public bool IsFixed { get; private set; }
    public bool IsSelected { get; private set; }
    public bool NameVisible => StockName.IsVisible;
    public Quote? LastQuote { get; private set; }
    public bool IsMarketClosed => LastQuote is { MarketStatus: "CLOSE" or "CLOSED", IsDemo: false };
    public string DisplayedPrice => PriceText.Text ?? "";
    public string StatusMessage { get; private set; } = "시세 연결 대기";
    public event EventHandler? SearchRequested;
    private bool leftButtonDown, rightButtonDown, closeOnRelease, closeQueued, closed;
    private bool searchPending, searchDispatchQueued, resetRightClickCount, leftDragging;
    private bool rightClickCompleted;
    private bool selectionGesture, groupRightAtPress;
    private readonly PopupSelection selection;
    private PixelPoint dragStartScreen, fixedPosition;
    private DateTimeOffset? latestSourceTime, lastResponseAt;
    private IPointer? capturedPointer;
    private ConnectionStatus status = new("시세 연결 대기");
    public QuoteWindow() : this(new("005930", "삼성전자", "KOSPI"), true) { }
    public QuoteWindow(Stock stock, bool showName) : this(stock, showName, new PopupSelection()) { }
    internal QuoteWindow(Stock stock, bool showName, PopupSelection selection)
    {
        this.selection = selection; InitializeComponent(); Stock = stock;
        StockName.Text = stock.Name; ToolTip.SetTip(StockName, stock.Name); SetNameVisible(showName);
        ActualThemeVariantChanged += (_, _) => RefreshQuoteColors();
        RefreshQuoteColors();
        AddHandler(PointerPressedEvent, PopupPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, PopupPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, PopupPointerMoved, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) =>
        {
            if (capturedPointer?.Captured == this) return;
            if (!leftButtonDown && !rightButtonDown && searchDispatchQueued)
            { capturedPointer = null; return; } // Avalonia releases implicit capture after the final up.
            if (leftButtonDown || rightButtonDown || searchPending) resetRightClickCount = true;
            if (leftButtonDown || rightButtonDown || searchPending) rightClickCompleted = false;
            selection.CancelMove(); selectionGesture = false;
            leftButtonDown = rightButtonDown = searchPending = closeOnRelease = leftDragging = false;
            capturedPointer = null;
        };
        Opened += (_, _) =>
        {
            selection.Register(this);
            PopupTopmost.Update(this);
            if (!OperatingSystem.IsWindows()) return;
            Win32Properties.AddWndProcHookCallback(this, WindowMessage);
            if (TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle && handle.Handle != IntPtr.Zero)
                SetWindowPos(handle.Handle, IntPtr.Zero, 0, 0, (int)Math.Round(Bounds.Width * RenderScaling),
                    (int)Math.Round(32 * RenderScaling), 0x0002 | 0x0004 | 0x0010);
        };
        Activated += (_, _) => PopupTopmost.QueueCheck();
        Deactivated += (_, _) => PopupTopmost.QueueCheck();
        PositionChanged += (_, _) => { if (IsFixed && Position != fixedPosition) Position = fixedPosition; };
        Closed += (_, _) =>
        {
            closed = true; ReleaseCapture();
            selection.Remove(this);
            PopupTopmost.Remove(this);
            if (OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(this, WindowMessage);
        };
    }
    public void SetNameVisible(bool visible)
    { StockName.IsVisible = visible; RefreshTitle(); }
    private void RefreshTitle() => Title = (NameVisible ? Stock.Name + " · StockViewer" : "StockViewer")
        + (IsSelected ? " · 선택됨" : "");
    public void SetFixed(bool enabled)
    { fixedPosition = Position; IsFixed = enabled; Topmost = enabled; PopupTopmost.Update(this); SetStatus(StatusMessage); }
    internal void SetSelected(bool enabled)
    { if (IsSelected == enabled) return; IsSelected = enabled; RefreshOutline(); RefreshTitle(); SetStatus(StatusMessage); }
    private void RefreshOutline() => Card.Classes.Set("selected", IsSelected);
    private IBrush ResourceBrush(string key) => (IBrush)this.FindResource(ActualThemeVariant, key)!;
    private void RefreshQuoteColors()
    {
        StockName.Foreground = ResourceBrush(IsMarketClosed ? "TextMutedBrush" : "TextPrimaryBrush");
        var key = LastQuote is null || IsMarketClosed ? "TextMutedBrush"
            : LastQuote.ChangePercent > 0 ? "RedBrush" : LastQuote.ChangePercent < 0 ? "BlueBrush" : "TextSecondaryBrush";
        PriceText.Foreground = PercentText.Foreground = ResourceBrush(key);
    }
    private void PreservePriceAndUpdateSession(Quote response, bool latestResponse)
    {
        if (latestResponse && LastQuote is { } previous && response.MarketStatus is "OPEN" or "CLOSE" or "CLOSED")
            LastQuote = previous with { MarketStatus = response.MarketStatus };
        RefreshStatus();
    }
    public void ApplyQuote(Quote quote)
    {
        if (quote.Code != Stock.Code) return;
        var latestResponse = !lastResponseAt.HasValue || quote.ReceivedAt >= lastResponseAt;
        if (!lastResponseAt.HasValue || quote.ReceivedAt > lastResponseAt) lastResponseAt = quote.ReceivedAt;
        if (LastQuote is { Source: "KRX", TradeTime: not null, SourceTime: null, IsDemo: false }
            && quote is { Source: "KRX", TradeTime: null, SourceTime: null, IsDemo: false }) { PreservePriceAndUpdateSession(quote, latestResponse); return; }
        if (latestSourceTime is { } last && quote.SourceTime is { } current && current < last) { PreservePriceAndUpdateSession(quote, latestResponse); return; }
        if (LastQuote is { } previous && quote.ReceivedAt < previous.ReceivedAt
            && (quote.SourceTime is null || previous.SourceTime is null || quote.SourceTime <= previous.SourceTime)) { PreservePriceAndUpdateSession(quote, latestResponse); return; }
        if (!latestResponse && LastQuote is { } retained)
            quote = quote with { MarketStatus = retained.MarketStatus };
        LastQuote = quote;
        if (quote.SourceTime.HasValue) latestSourceTime = quote.SourceTime;
        PriceText.Text = quote.Currency == "USD" ? "$" + quote.Price.ToString("#,##0.00##", CultureInfo.InvariantCulture)
            : quote.Price.ToString("N0", CultureInfo.GetCultureInfo("ko-KR")) + "원";
        PercentText.Text = $"{(quote.ChangePercent > 0 ? "+" : "")}{quote.ChangePercent:0.00}%";
        RefreshStatus();
    }
    public void SetConnection(ConnectionStatus connection)
    { if (connection.Code is not null && connection.Code != Stock.Code) return; status = connection; RefreshStatus(); }
    public void ShowError(string message) => SetStatus(message);
    public void ResetQuote()
    {
        LastQuote = null; latestSourceTime = lastResponseAt = null; PriceText.Text = PercentText.Text = "—";
        RefreshStatus();
    }
    private void SetStatus(string message)
    {
        StatusMessage = message;
        Avalonia.Automation.AutomationProperties.SetHelpText(this,
            (IsSelected ? "선택됨 · " : "") + (IsFixed ? "맨 위 고정 · 위치 고정" : "고정 해제 · 드래그로 이동"));
        ToolTip.SetTip(Card, message + $"\n{(IsFixed ? "Fix" : "Free")}{(IsSelected ? " · 선택됨" : "")} · Ctrl+클릭: 선택 전환\n우클릭: 선택 창 고정 전환 · 우클릭 두 번: 단일 창 닫기\n좌우 버튼 동시 클릭: 근처에 검색창 표시");
    }
    private void RefreshStatus()
    {
        RefreshQuoteColors();
        if (LastQuote is { IsDemo: true }) { SetStatus("DEMO · 예시 가격 · 실제 시세 아님"); return; }
        if (!status.Connected)
        {
            SetStatus(LastQuote is null ? status.Message : status.Message + " · 마지막 수신 가격 "
                + (lastResponseAt ?? LastQuote.ReceivedAt).ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss")); return;
        }
        if (LastQuote is null) { SetStatus("시세를 불러오고 있어요…"); return; }
        var received = " · 수신 " + (lastResponseAt ?? LastQuote.ReceivedAt).ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss");
        if (LastQuote.SourceTime is { } time)
        {
            SetStatus(LastQuote.Source + (LastQuote.MarketStatus == "CLOSE" ? " · 장 마감" : "")
                + " · 기준 " + time.ToOffset(TimeSpan.FromHours(9)).ToString("MM.dd HH:mm:ss") + received); return;
        }
        SetStatus(LastQuote.TradeTime is { Length: 6 } trade ? $"{LastQuote.Source} · 최근 체결 {trade[..2]}:{trade[2..4]}:{trade[4..]}" + received
            : LastQuote.Source + received + " · 체결 시각 미제공");
    }
    public void ProcessMouseDown(MouseButton button, int clickCount = 1, bool otherButtonPressed = false, PixelPoint? screenPosition = null,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        if (closed || closeQueued || !IsVisible || button is not (MouseButton.Left or MouseButton.Right)) return;
        if (button == MouseButton.Left)
        {
            leftButtonDown = true; rightButtonDown |= otherButtonPressed;
            dragStartScreen = screenPosition ?? Position; leftDragging = false; resetRightClickCount = true;
            rightClickCompleted = false;
        }
        else
        {
            rightButtonDown = true; leftButtonDown |= otherButtonPressed;
            closeOnRelease = rightClickCompleted && !resetRightClickCount && clickCount >= 2; resetRightClickCount = false;
        }
        if (leftButtonDown && rightButtonDown || searchPending) BeginSearchGesture();
        else if ((modifiers & (KeyModifiers.Control | (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.None))) != 0
            && (button == MouseButton.Left || OperatingSystem.IsMacOS()))
        {
            selection.Toggle(this); selection.CancelMove(); selectionGesture = true;
            closeOnRelease = rightClickCompleted = false;
        }
        else if (button == MouseButton.Left) { selectionGesture = false; selection.BeginMove(this); }
        else
        {
            selectionGesture = false; groupRightAtPress = selection.IsGroup(this);
            if (groupRightAtPress) closeOnRelease = false;
        }
        capturedPointer?.Capture(this);
    }
    private void BeginSearchGesture()
    { searchPending = true; closeOnRelease = false; resetRightClickCount = true; rightClickCompleted = false; leftDragging = false; selection.CancelMove(); }
    public void ProcessMouseUp(MouseButton button)
    {
        if (closed || button is not (MouseButton.Left or MouseButton.Right)) return;
        var wasDown = button == MouseButton.Left ? leftButtonDown : rightButtonDown;
        if (button == MouseButton.Left) { leftButtonDown = false; leftDragging = false; selection.CancelMove(); } else rightButtonDown = false;
        if (searchPending)
        { if (!leftButtonDown && !rightButtonDown) QueueSearchRequest(); return; }
        if (selectionGesture)
        { if (!leftButtonDown && !rightButtonDown) { selectionGesture = false; ReleaseCapture(); } return; }
        if (closeOnRelease)
        {
            if (leftButtonDown || rightButtonDown || closeQueued) return;
            closeOnRelease = false; closeQueued = true;
            Dispatcher.UIThread.Post(() => { if (!closed) { ReleaseCapture(); Close(); } }, DispatcherPriority.Background); return;
        }
        if (button == MouseButton.Right && wasDown) { rightClickCompleted = !groupRightAtPress; selection.RightClick(this); }
        if (!leftButtonDown && !rightButtonDown) ReleaseCapture();
    }
    private void ReleaseCapture()
    { var pointer = capturedPointer; capturedPointer = null; if (pointer?.Captured == this) pointer.Capture(null); }
    private void QueueSearchRequest()
    {
        if (searchDispatchQueued) return;
        searchDispatchQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            searchDispatchQueued = false;
            if (closed || !searchPending || leftButtonDown || rightButtonDown) return;
            searchPending = false; ReleaseCapture(); SearchRequested?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Background);
    }
    public void ProcessMouseMove(PixelPoint screenPosition, bool leftPressed, bool rightPressed)
    {
        if (closed || closeQueued || searchPending || selectionGesture || !leftButtonDown || !leftPressed) return;
        if (rightButtonDown || rightPressed) { rightButtonDown = true; BeginSearchGesture(); return; }
        if (IsFixed) return;
        var dx = screenPosition.X - dragStartScreen.X; var dy = screenPosition.Y - dragStartScreen.Y;
        if (!leftDragging && Math.Abs(dx) < 4 * RenderScaling && Math.Abs(dy) < 4 * RenderScaling) return;
        leftDragging = true; selection.Move(this, dx, dy);
    }
    private void PopupPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed) return;
        e.Handled = true; SyncPointerButtons(e.Pointer, point, e.ClickCount, e.KeyModifiers);
    }
    private void PopupPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!leftButtonDown && !rightButtonDown) return;
        e.Handled = true; SyncPointerButtons(e.Pointer, e.GetCurrentPoint(this), 1, e.KeyModifiers);
    }
    private void PopupPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!leftButtonDown && !rightButtonDown) return;
        e.Handled = true; var point = e.GetCurrentPoint(this);
        // Avalonia delivers additional button presses and partial releases as
        // PointerMoved until all buttons are up. Inspect the full button state.
        SyncPointerButtons(e.Pointer, point, 1, e.KeyModifiers);
        ProcessMouseMove(this.PointToScreen(point.Position), point.Properties.IsLeftButtonPressed, point.Properties.IsRightButtonPressed);
    }
    private void SyncPointerButtons(IPointer pointer, PointerPoint point, int clickCount, KeyModifiers modifiers)
    {
        capturedPointer = pointer;
        var left = point.Properties.IsLeftButtonPressed; var right = point.Properties.IsRightButtonPressed;
        var position = this.PointToScreen(point.Position);
        // Fast/native drags may coalesce movement into the final release. Apply
        // that position before clearing the drag state, keeping the whole group
        // aligned even when no intermediate PointerMoved event was delivered.
        if (!left && leftButtonDown && !right && !rightButtonDown)
            ProcessMouseMove(position, true, false);
        if (left && !leftButtonDown) ProcessMouseDown(MouseButton.Left, clickCount, right, position, modifiers);
        if (right && !rightButtonDown) ProcessMouseDown(MouseButton.Right, clickCount, left, position, modifiers);
        if (!left && leftButtonDown) ProcessMouseUp(MouseButton.Left);
        if (!right && rightButtonDown) ProcessMouseUp(MouseButton.Right);
    }
    private void PopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (leftButtonDown || rightButtonDown) { searchPending = false; closeOnRelease = true; } else Close();
    }
    private IntPtr WindowMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x007B) { handled = true; return IntPtr.Zero; }
        if (msg == 0x0046 && lParam != IntPtr.Zero)
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            if (IsFixed)
            {
                position.Flags |= 0x0002;
                // Activation, resizing and other native ordering requests must
                // never demote one pinned quote while another stays topmost.
                if ((position.Flags & 0x0004) == 0) position.InsertAfter = new(-1);
            }
            if ((position.Flags & 0x0001) == 0) position.Height = (int)Math.Round(32 * RenderScaling);
            Marshal.StructureToPtr(position, lParam, false); handled = true;
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
    { public IntPtr Hwnd, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
