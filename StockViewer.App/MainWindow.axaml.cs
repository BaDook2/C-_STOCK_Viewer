using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockViewer.Core;

namespace StockViewer.Desktop;

public partial class MainWindow : Window
{
    private readonly SettingsStore settings;
    private readonly Dictionary<string, QuoteWindow> popups = [];
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource session = new();
    private IReadOnlyList<Stock> stocks;
    private IMarketDataService? service;
    private ConnectionStatus connection = new("공개 시세 연결 대기 · API 키 불필요");
    private CancellationTokenSource? searchCancellation;
    private Task? pendingSearch;
    private bool suppressSearch, closing, initialized, searchHidden, windowClosed, loaded;
    private long searchVisibilityVersion;
    public event EventHandler? SessionEnded;
    public IReadOnlyDictionary<string, QuoteWindow> Popups => popups;
    public string StatusMessage => connection.Message;
    public MainWindow() : this(null, true) { }
    public MainWindow(SettingsStore? store, bool autoConnect = true)
    {
        settings = store ?? new SettingsStore();
        stocks = settings.LoadCatalog();
        if (stocks.Count == 0) stocks = DemoMarketData.SampleStocks;
        InitializeComponent();
        DisplayName.IsChecked = settings.LoadDisplayName(); initialized = true;
        SearchBox.PropertyChanged += (_, e) =>
        { if (e.Property == TextBox.TextProperty && !suppressSearch) RefreshSuggestions(true); };
        Deactivated += (_, _) => DismissSuggestions();
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is not Visual visual) return;
            var ancestors = visual.GetSelfAndVisualAncestors().ToArray();
            if (!ancestors.Contains(SearchBox) && !ancestors.Contains(SearchResults)) DismissSuggestions();
        }, RoutingStrategies.Tunnel);
        SearchResults.AddHandler(PointerReleasedEvent, ResultClicked, RoutingStrategies.Tunnel, true);
        SearchBox.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(SearchBox).Properties.IsLeftButtonPressed) return;
            SearchBox.Focus(); RefreshSuggestions(openDropdown: true);
        }, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            if (loaded) return;
            loaded = true; SearchBox.Focus();
            if (!autoConnect) return;
            try { await UsePublicServiceAsync(); }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception e) when (IsDataError(e) || e is UnauthorizedAccessException)
            { if (!closing) ShowStatus(new("공개 시세 연결 실패 · 네트워크를 확인해 주세요")); }
        };
        Closing += (_, e) =>
        {
            if (closing || popups.Count == 0) return;
            e.Cancel = true; searchVisibilityVersion++; searchHidden = true;
            DismissSuggestions(); CancelPendingSearch(); Hide();
        };
        Closed += async (_, _) =>
        {
            windowClosed = true; searchHidden = true; DismissSuggestions(); CancelPendingSearch();
            if (popups.Count == 0) await EndSessionAsync();
        };
    }
    private static bool IsDataError(Exception e) => e is IOException or InvalidDataException or InvalidOperationException
        or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException;
    private async Task EndSessionAsync()
    {
        if (closing) return;
        closing = true; lifetime.Cancel(); session.Cancel();
        try { if (service is not null) await service.DisposeAsync(); }
        finally
        {
            session.Dispose(); lifetime.Dispose();
            if (!windowClosed) Close();
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }
    private void ShowSearchNearQuote(object? sender, EventArgs e)
    {
        if (closing || windowClosed || sender is not QuoteWindow popup || !popup.IsVisible
            || !popups.TryGetValue(popup.Stock.Code, out var current) || !ReferenceEquals(current, popup)) return;
        searchHidden = false; DismissSuggestions();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        PopupPlacement.BelowQuote(this, popup);
        if (!IsVisible) Show();
        PopupPlacement.BelowQuote(this, popup);
        Activate(); SearchBox.Focus();
    }
    private async Task UsePublicServiceAsync()
    {
        var client = new PublicMarketData();
        await UseServiceAsync(client, stocks);
        if (closing || searchHidden) return;
        _ = RefreshPublicCatalogAsync(client, session.Token);
    }
    private async Task RefreshPublicCatalogAsync(IMarketDataService client, CancellationToken cancellation)
    {
        try
        {
            var catalog = await client.GetStocksAsync(cancellation);
            if (closing || searchHidden || !ReferenceEquals(service, client)) return;
            stocks = catalog.Concat(stocks).DistinctBy(s => s.Code).ToArray();
            settings.SaveCatalog(stocks); RefreshSuggestions();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (IsDataError(e) || e is UnauthorizedAccessException) { }
    }
    public async Task UseServiceAsync(IMarketDataService client, IReadOnlyList<Stock> catalog)
    {
        if (closing) { await client.DisposeAsync(); return; }
        CancelPendingSearch(); session.Cancel(); session.Dispose(); session = new();
        var previous = service; service = client; stocks = catalog;
        if (previous is not null) await previous.DisposeAsync();
        if (closing) { await client.DisposeAsync(); return; }
        foreach (var popup in popups.Values) popup.ResetQuote();
        client.QuoteReceived += quote => DispatchFromService(client, () =>
        { if (popups.TryGetValue(quote.Code, out var popup)) popup.ApplyQuote(quote); });
        client.StatusChanged += status => DispatchFromService(client, () => ShowStatus(status));
        ShowStatus(new(client.IsDemo ? "데모 모드 · 예시 가격" : "공개 시세 연결 · API 키 불필요", client.IsDemo, client.IsDemo));
        client.Start(); await UpdateSubscriptionsAsync();
        foreach (var popup in popups.Values.ToArray()) _ = LoadQuoteAsync(popup, client, session.Token);
        if (!client.IsDemo)
        {
            try { settings.SaveCatalog(catalog); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowStatus(new("연결 완료 · 종목 목록 저장 실패", true)); }
        }
        RefreshSuggestions(); Suggestions.IsOpen = false;
    }
    private void ShowStatus(ConnectionStatus status)
    {
        if (status.Code is { } code)
        { if (popups.TryGetValue(code, out var popup)) popup.SetConnection(status); return; }
        connection = status;
        foreach (var popup in popups.Values) popup.SetConnection(status);
    }
    private void DispatchFromService(IMarketDataService client, Action action)
    {
        if (closing || !ReferenceEquals(service, client)) return;
        Dispatcher.UIThread.Post(() => { if (!closing && ReferenceEquals(service, client)) action(); });
    }
    private async Task UpdateSubscriptionsAsync()
    {
        var current = service;
        if (current is null || closing) return;
        try { await current.SetSubscriptionsAsync(popups.Keys.ToArray(), session.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) when (IsDataError(e))
        { if (ReferenceEquals(service, current)) ShowStatus(new("구독 변경 실패 · 재연결 대기")); }
    }
    public async Task<QuoteWindow?> OpenStockAsync(Stock stock)
    {
        if (closing || searchHidden) return null;
        if (!popups.ContainsKey(stock.Code) && popups.Count >= 200)
        { ShowStatus(new("동시에 최대 200개 종목까지 표시할 수 있어요.", connection.Connected, connection.IsDemo)); return null; }
        if (popups.Remove(stock.Code, out var old)) old.Close();
        var popup = new QuoteWindow(stock, DisplayName.IsChecked == true);
        popups[stock.Code] = popup; popup.SearchRequested += ShowSearchNearQuote;
        popup.Closed += async (_, _) =>
        {
            popup.SearchRequested -= ShowSearchNearQuote;
            if (!popups.TryGetValue(stock.Code, out var current) || !ReferenceEquals(current, popup)) return;
            popups.Remove(stock.Code);
            if (searchHidden && popups.Count == 0) await EndSessionAsync();
            else await UpdateSubscriptionsAsync();
        };
        popup.SetConnection(connection);
        popup.Measure(Size.Infinity); PopupPlacement.BelowSearch(popup, this);
        popup.Show(); PopupPlacement.BelowSearch(popup, this); DismissSuggestions();
        await UpdateSubscriptionsAsync();
        if (service is { } currentService) await LoadQuoteAsync(popup, currentService, session.Token);
        else popup.ShowError("시세 연결을 기다리는 중입니다.");
        if (!searchHidden && popup.IsVisible && !popup.IsFixed) PopupPlacement.BelowSearch(popup, this);
        return popup;
    }
    private async Task LoadQuoteAsync(QuoteWindow popup, IMarketDataService client, CancellationToken cancellation)
    {
        try
        {
            var quote = await client.GetSnapshotAsync(popup.Stock.Code, cancellation);
            if (!closing && ReferenceEquals(service, client) && popups.TryGetValue(popup.Stock.Code, out var current)
                && ReferenceEquals(current, popup)) popup.ApplyQuote(quote);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (IsDataError(e))
        {
            if (!closing && ReferenceEquals(service, client) && popups.TryGetValue(popup.Stock.Code, out var current)
                && ReferenceEquals(current, popup)) popup.ShowError("시세 조회 실패 · 네트워크를 확인해 주세요");
        }
    }
    private void CancelPendingSearch()
    {
        var previous = searchCancellation; searchCancellation = null; pendingSearch = null;
        if (previous is null) return;
        previous.Cancel(); previous.Dispose();
    }
    private void RefreshSuggestions(bool openDropdown = false)
    {
        if (!initialized || searchHidden || closing) return;
        var query = SearchBox.Text ?? "";
        SearchPlaceholder.IsVisible = query.Length == 0; CancelPendingSearch();
        var found = StockSearch.Find(stocks, query);
        SearchResults.ItemsSource = found; SearchResults.SelectedIndex = found.Length == 0 ? -1 : 0;
        NoResults.IsVisible = found.Length == 0;
        Suggestions.IsOpen = (openDropdown || Suggestions.IsOpen) && IsActive && SearchBox.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(query);
        if (service is IStockSearchService remote && !string.IsNullOrWhiteSpace(query))
        {
            searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            pendingSearch = CompleteSuggestionsAsync(remote, query, searchCancellation.Token);
        }
    }
    private async Task CompleteSuggestionsAsync(IStockSearchService remote, string query, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(250, cancellation);
            var results = await remote.SearchAsync(query, cancellation);
            if (closing || searchHidden || cancellation.IsCancellationRequested || !ReferenceEquals(service, remote) || SearchBox.Text != query) return;
            var selected = (SearchResults.SelectedItem as Stock)?.Code;
            stocks = results.Concat(stocks).DistinctBy(s => s.Code).ToArray();
            var matches = results.Concat(StockSearch.Find(stocks, query)).DistinctBy(s => s.Code).Take(12).ToArray();
            SearchResults.ItemsSource = matches;
            SearchResults.SelectedIndex = matches.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(matches, s => s.Code == selected));
            NoResults.IsVisible = matches.Length == 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (IsDataError(e)) { }
    }
    private void SearchUnfocused(object? sender, RoutedEventArgs e) => DismissSuggestions();
    private void DismissSuggestions() => Suggestions.IsOpen = false;
    private async Task OpenSelectionAsync()
    {
        if (closing || searchHidden) return;
        var query = StockSearch.Normalize(SearchBox.Text ?? ""); var visibilityVersion = searchVisibilityVersion;
        if (query.Length == 0) { SearchBox.Focus(); return; }
        if (pendingSearch is { } pending) await pending;
        if (closing || searchHidden || visibilityVersion != searchVisibilityVersion || query != StockSearch.Normalize(SearchBox.Text ?? "")) return;
        var found = StockSearch.Find(stocks, SearchBox.Text ?? "");
        var chosen = found.FirstOrDefault(s => StockSearch.IsExactMatch(s, query)) ?? SearchResults.SelectedItem as Stock ?? found.FirstOrDefault();
        if (chosen is null) { Suggestions.IsOpen = true; return; }
        await SelectAndOpenAsync(chosen);
    }
    private async Task SelectAndOpenAsync(Stock stock)
    {
        if (closing || searchHidden) return;
        CancelPendingSearch(); suppressSearch = true; SearchBox.Text = stock.Name;
        SearchBox.CaretIndex = stock.Name.Length; suppressSearch = false;
        SearchPlaceholder.IsVisible = false; DismissSuggestions(); await OpenStockAsync(stock);
    }
    private async void SearchClicked(object? sender, RoutedEventArgs e) => await OpenSelectionAsync();
    private async void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await OpenSelectionAsync(); }
        else if (e.Key is Key.Down or Key.Up)
        {
            e.Handled = true; if (SearchResults.ItemCount == 0) return;
            if (!Suggestions.IsOpen) RefreshSuggestions(true);
            else SearchResults.SelectedIndex = Math.Clamp(SearchResults.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, SearchResults.ItemCount - 1);
            SearchResults.ScrollIntoView(SearchResults.SelectedItem!);
        }
        else if (e.Key == Key.Escape) { e.Handled = true; DismissSuggestions(); }
    }
    private async void ResultClicked(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var visual = SearchResults.InputHitTest(e.GetPosition(SearchResults)) as Visual ?? e.Source as Visual;
        if (visual is null) return;
        var item = visual as ListBoxItem ?? visual.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if ((item?.DataContext as Stock ?? item?.Content as Stock) is { } stock)
        { e.Handled = true; await SelectAndOpenAsync(stock); }
    }
    private void DisplayNameChanged(object? sender, RoutedEventArgs e)
    {
        try { settings.SaveDisplayName(DisplayName.IsChecked == true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ShowStatus(new("표시 설정을 저장하지 못했습니다.", connection.Connected, connection.IsDemo)); }
    }
    private void DragHeader(object? sender, PointerPressedEventArgs e)
    { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { e.Handled = true; DismissSuggestions(); BeginMoveDrag(e); } }
    private void Minimize(object? sender, RoutedEventArgs e) { DismissSuggestions(); WindowState = WindowState.Minimized; }
    private void CloseApp(object? sender, RoutedEventArgs e)
    { e.Handled = true; DismissSuggestions(); if (!searchHidden) Close(); }
}
