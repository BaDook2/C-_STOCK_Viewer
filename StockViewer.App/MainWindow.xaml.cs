using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockViewer.Core;

namespace StockViewer.App;

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
    private bool suppressSearch, closing, initialized, searchClosed;
    public event EventHandler? SessionEnded;
    public IReadOnlyDictionary<string, QuoteWindow> Popups => popups;
    public string StatusMessage => connection.Message;
    public MainWindow(SettingsStore? store = null, bool autoConnect = true)
    {
        settings = store ?? new SettingsStore();
        stocks = settings.LoadCatalog();
        if (stocks.Count == 0) stocks = DemoMarketData.SampleStocks;
        InitializeComponent();
        DisplayName.IsChecked = settings.LoadDisplayName();
        initialized = true;
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            if (!autoConnect) return;
            try
            {
                await UsePublicServiceAsync();
            }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            { if (!closing) ShowStatus(new("공개 시세 연결 실패 · 네트워크를 확인해 주세요")); }
            finally { if (!closing) SettingsButton.IsEnabled = true; }
        };
        Closed += OnClosed;
    }
    private async void OnClosed(object? sender, EventArgs e)
    {
        searchClosed = true; Suggestions.IsOpen = false;
        CancelPendingSearch();
        if (popups.Count == 0) await EndSessionAsync();
    }
    private async Task EndSessionAsync()
    {
        if (closing) return;
        closing = true; lifetime.Cancel(); session.Cancel();
        try { if (service is not null) await service.DisposeAsync(); }
        finally
        {
            session.Dispose(); lifetime.Dispose();
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }
    private async Task UsePublicServiceAsync()
    {
        var client = new PublicMarketData();
        await UseServiceAsync(client, stocks);
        if (closing || searchClosed) return;
        _ = RefreshPublicCatalogAsync(client, session.Token);
    }
    private async Task RefreshPublicCatalogAsync(IMarketDataService client, CancellationToken cancellation)
    {
        try
        {
            var catalog = await client.GetStocksAsync(cancellation);
            if (closing || searchClosed || !ReferenceEquals(service, client)) return;
            stocks = catalog.Concat(stocks).DistinctBy(s => s.Code).ToArray();
            settings.SaveCatalog(stocks);
            RefreshSuggestions();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        { /* Cached names and remote autocomplete still work if the initial list is unavailable. */ }
    }
    public async Task UseServiceAsync(IMarketDataService client, IReadOnlyList<Stock> catalog)
    {
        if (closing) { await client.DisposeAsync(); return; }
        CancelPendingSearch();
        session.Cancel(); session.Dispose(); session = new CancellationTokenSource();
        var previous = service; service = client; stocks = catalog;
        if (previous is not null) await previous.DisposeAsync();
        if (closing) { await client.DisposeAsync(); return; }
        foreach (var popup in popups.Values) popup.ResetQuote();
        client.QuoteReceived += quote => Dispatcher.BeginInvoke(() =>
        {
            if (!closing && ReferenceEquals(service, client) && popups.TryGetValue(quote.Code, out var popup)) popup.ApplyQuote(quote);
        });
        client.StatusChanged += status => Dispatcher.BeginInvoke(() =>
        { if (!closing && ReferenceEquals(service, client)) ShowStatus(status); });
        ShowStatus(new(client.IsDemo ? "데모 모드 · 예시 가격" : "공개 시세 연결 · API 키 불필요", client.IsDemo, client.IsDemo));
        client.Start();
        await UpdateSubscriptionsAsync();
        foreach (var popup in popups.Values.ToArray()) _ = LoadQuoteAsync(popup, client, session.Token);
        if (!client.IsDemo)
        {
            try { settings.SaveCatalog(catalog); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowStatus(new("연결 완료 · 종목 목록 저장 실패", true)); }
        }
        RefreshSuggestions();
        Suggestions.IsOpen = false;
    }
    private void ShowStatus(ConnectionStatus status)
    {
        connection = status;
        SettingsButton.ToolTip = status.Message + "\n공개 시세 설정";
        foreach (var popup in popups.Values) popup.SetConnection(status);
    }
    private async Task UpdateSubscriptionsAsync()
    {
        var current = service;
        if (current is null || closing) return;
        try { await current.SetSubscriptionsAsync(popups.Keys.ToArray(), session.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Net.WebSockets.WebSocketException)
        { if (ReferenceEquals(service, current)) ShowStatus(new("구독 변경 실패 · 재연결 대기")); }
    }
    public async Task<QuoteWindow?> OpenStockAsync(Stock stock)
    {
        if (closing || searchClosed) return null;
        if (!popups.ContainsKey(stock.Code) && popups.Count >= 200)
        { ShowStatus(new("동시에 최대 200개 종목까지 표시할 수 있어요.", connection.Connected, connection.IsDemo)); return null; }
        if (popups.Remove(stock.Code, out var old)) old.Close();
        var popup = new QuoteWindow(stock, DisplayName.IsChecked == true);
        popups[stock.Code] = popup;
        popup.Closed += async (_, _) =>
        {
            if (popups.TryGetValue(stock.Code, out var current) && ReferenceEquals(current, popup))
            {
                popups.Remove(stock.Code);
                if (searchClosed && popups.Count == 0) await EndSessionAsync();
                else await UpdateSubscriptionsAsync();
            }
        };
        popup.SetConnection(connection);
        // Layout before Show prevents an initial flash at the screen origin.
        popup.Measure(new Size(double.PositiveInfinity, popup.Height));
        popup.Left = Left + (ActualWidth - popup.DesiredSize.Width) / 2;
        popup.Top = Top + ActualHeight + 4;
        popup.ContentRendered += (_, _) => { if (!searchClosed && popup.IsVisible) PopupPlacement.BelowSearch(popup, this); };
        popup.Show(); PopupPlacement.BelowSearch(popup, this);
        Suggestions.IsOpen = false;
        await UpdateSubscriptionsAsync();
        if (service is { } currentService) await LoadQuoteAsync(popup, currentService, session.Token);
        else popup.ShowError("시세 연결을 기다리는 중입니다.");
        return popup;
    }
    private async Task LoadQuoteAsync(QuoteWindow popup, IMarketDataService client, CancellationToken cancellation)
    {
        try
        {
            var quote = await client.GetSnapshotAsync(popup.Stock.Code, cancellation);
            if (!closing && ReferenceEquals(service, client) && popups.TryGetValue(popup.Stock.Code, out var current) && ReferenceEquals(current, popup)) popup.ApplyQuote(quote);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        { if (!closing && ReferenceEquals(service, client) && popups.TryGetValue(popup.Stock.Code, out var current) && ReferenceEquals(current, popup)) popup.ShowError("시세 조회 실패 · 설정과 네트워크를 확인해 주세요"); }
    }
    private void CancelPendingSearch()
    {
        var previous = searchCancellation;
        searchCancellation = null;
        pendingSearch = null;
        // Empty queries do not create a replacement. Clear the field before
        // disposing so the next input/focus event cannot cancel a disposed CTS.
        if (previous is null) return;
        previous.Cancel();
        previous.Dispose();
    }
    private void RefreshSuggestions()
    {
        if (!initialized || searchClosed || closing) return;
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CancelPendingSearch();
        var found = StockSearch.Find(stocks, SearchBox.Text);
        SearchResults.ItemsSource = found; SearchResults.SelectedIndex = found.Length == 0 ? -1 : 0;
        NoResults.Visibility = found.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Suggestions.IsOpen = SearchBox.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(SearchBox.Text);
        pendingSearch = null;
        if (service is IStockSearchService remote && !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            pendingSearch = CompleteSuggestionsAsync(remote, SearchBox.Text, searchCancellation.Token);
        }
    }
    private async Task CompleteSuggestionsAsync(IStockSearchService remote, string query, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(250, cancellation);
            var results = await remote.SearchAsync(query, cancellation);
            if (closing || searchClosed || cancellation.IsCancellationRequested || !ReferenceEquals(service, remote) || SearchBox.Text != query) return;
            var selectedCode = (SearchResults.SelectedItem as Stock)?.Code;
            stocks = results.Concat(stocks).DistinctBy(s => s.Code).ToArray();
            var matches = results.Concat(StockSearch.Find(stocks, query)).DistinctBy(s => s.Code).Take(12).ToArray();
            SearchResults.ItemsSource = matches;
            SearchResults.SelectedIndex = matches.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(matches, s => s.Code == selectedCode));
            NoResults.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        { /* Keep cached local suggestions when public search is unavailable. */ }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (!suppressSearch) RefreshSuggestions(); }
    private void SearchFocused(object sender, KeyboardFocusChangedEventArgs e) => RefreshSuggestions();
    private async Task OpenSelectionAsync()
    {
        var query = StockSearch.Normalize(SearchBox.Text);
        if (query.Length == 0) { SearchBox.Focus(); return; }
        if (pendingSearch is { } pending) await pending;
        if (closing || query != StockSearch.Normalize(SearchBox.Text)) return;
        var found = StockSearch.Find(stocks, SearchBox.Text);
        var chosen = found.FirstOrDefault(s => s.Code == query || StockSearch.Normalize(s.Name) == query)
            ?? (SearchResults.SelectedItem as Stock) ?? found.FirstOrDefault();
        if (chosen is null) { Suggestions.IsOpen = true; return; }
        await SelectAndOpenAsync(chosen);
    }
    private async Task SelectAndOpenAsync(Stock stock)
    {
        CancelPendingSearch();
        suppressSearch = true; SearchBox.Text = stock.Name; SearchBox.CaretIndex = SearchBox.Text.Length; suppressSearch = false;
        SearchPlaceholder.Visibility = Visibility.Collapsed; Suggestions.IsOpen = false;
        await OpenStockAsync(stock);
    }
    private async void SearchClicked(object sender, RoutedEventArgs e) => await OpenSelectionAsync();
    private async void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await OpenSelectionAsync(); }
        else if (e.Key is Key.Down or Key.Up)
        {
            e.Handled = true;
            if (SearchResults.Items.Count == 0) return;
            if (!Suggestions.IsOpen) RefreshSuggestions();
            else SearchResults.SelectedIndex = Math.Clamp(SearchResults.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, SearchResults.Items.Count - 1);
            SearchResults.ScrollIntoView(SearchResults.SelectedItem);
        }
        else if (e.Key == Key.Escape) { e.Handled = true; Suggestions.IsOpen = false; }
    }
    private async void ResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SearchResults, e.OriginalSource as DependencyObject) is ListBoxItem { Content: Stock stock })
        { e.Handled = true; await SelectAndOpenAsync(stock); }
    }
    private async void ResultKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && SearchResults.SelectedItem is Stock stock) { e.Handled = true; await SelectAndOpenAsync(stock); } else if (e.Key == Key.Escape) { Suggestions.IsOpen = false; SearchBox.Focus(); } }
    private void DisplayNameChanged(object sender, RoutedEventArgs e)
    {
        try { settings.SaveDisplayName(DisplayName.IsChecked == true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowStatus(new("표시 설정을 저장하지 못했습니다.", connection.Connected, connection.IsDemo)); }
    }
    private async void OpenSettings(object sender, RoutedEventArgs e)
    {
        Suggestions.IsOpen = false;
        var dialog = new SettingsWindow(connection.Message) { Owner = this };
        if (dialog.ShowDialog() == true) await UsePublicServiceAsync();
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    { if (e.ButtonState == MouseButtonState.Pressed) { Suggestions.IsOpen = false; DragMove(); } }
    private void Minimize(object sender, RoutedEventArgs e) { Suggestions.IsOpen = false; WindowState = WindowState.Minimized; }
    private void CloseApp(object sender, RoutedEventArgs e) => Close();
}
