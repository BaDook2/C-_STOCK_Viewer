using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StockViewer.Core;

namespace StockViewer.Desktop;

public partial class DataDelayWindow : Window
{
    private readonly Dictionary<string, Stock> stocks = [];
    public DataDelayWindow() => InitializeComponent();
    public void AddStock(Stock stock)
    {
        stocks[stock.Code] = stock;
        StockNames.Text = string.Join(" · ", stocks.Values.Select(s => s.Name + " (" + s.DisplayCode + ")"));
    }
    private void Dismiss(object? sender, RoutedEventArgs e) => Close();
    private void NoticeKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }
}
