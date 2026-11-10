using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StockViewer.Desktop;

public partial class LayoutNoticeWindow : Window
{
    public LayoutNoticeWindow() => InitializeComponent();
    public void SetMessage(string message) => Message.Text = message;
    private void Dismiss(object? sender, RoutedEventArgs e) => Close();
    private void NoticeKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }
}
