using System.Windows;
using System.Windows.Input;

namespace StockViewer.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(string status)
    {
        InitializeComponent();
        CurrentStatus.Text = status;
    }
    private void Reconnect(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel(object sender, RoutedEventArgs e) => Close();
    private void DragHeader(object sender, MouseButtonEventArgs e)
    { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
