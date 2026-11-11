using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static async Task LayoutFeedbackTests()
    {
        var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        try
        {
            await main.OpenStockAsync(DemoMarketData.SampleStocks[0]);
            var save = Find<Button>(main, "SaveLayoutButton"); var load = Find<Button>(main, "LoadLayoutButton");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(save.Classes.Contains("layout-feedback") && !load.Classes.Contains("layout-feedback") && main.OwnedWindows.Count == 0,
                "saving shows red feedback on its own button without creating a notice window");
            foreach (var dark in new[] { false, true })
            {
                Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; await Idle();
                Check(BrushColor(save.Foreground) == ThemeColor(save, "RedBrush")
                    && save.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().All(p => BrushColor(p.Stroke) == ThemeColor(save, "RedBrush")),
                    "save icon feedback stays red in the current theme");
            }
            load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(load.Classes.Contains("layout-feedback") && main.Popups.Count == 1 && main.OwnedWindows.Count == 0,
                "loading restores the layout and highlights its button without a notification popup");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(600);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(600);
            Check(save.Classes.Contains("layout-feedback") && !load.Classes.Contains("layout-feedback"),
                "a repeated save restarts its one-second feedback without extending the load button feedback");
            await Task.Delay(550);
            Check(!save.Classes.Contains("layout-feedback") && BrushColor(save.Foreground) == ThemeColor(save, "TextSecondaryBrush"),
                "the button returns to its normal colour one second after its most recent click");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); HideSearch(main); await Idle();
            Check(!save.Classes.Contains("layout-feedback") && main.Popups.Count == 1,
                "hiding search clears pending feedback while keeping quotes open");
        }
        finally { main.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Light; }
        var empty = MakeMain();
        try
        {
            Find<Button>(empty, "SaveLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<Button>(empty, "LoadLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(empty.OwnedWindows.Count == 0 && empty.Popups.Count == 0,
                "missing layouts and empty saves never create layout notice popups");
        }
        finally { empty.Close(); }
    }
    private static async Task TaskbarPersistenceTests()
    {
        var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        try
        {
            var first = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
            var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!; first.SetFixed(true);
            var missingEntries = 0;
            void ObserveTaskbar(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if ((e.Property == Window.IsVisibleProperty || e.Property == Window.ShowInTaskbarProperty)
                    && !(main.IsVisible && main.ShowInTaskbar)
                    && !main.Popups.Values.Any(p => p.IsVisible && p.ShowInTaskbar)) missingEntries++;
            }
            main.PropertyChanged += ObserveTaskbar;
            first.PropertyChanged += ObserveTaskbar;
            second.PropertyChanged += ObserveTaskbar;
            Check(main.Popups.Values.All(p => !p.ShowInTaskbar), "visible search is the only taskbar entry");
            HideSearch(main); await Idle();
            Check(!main.IsVisible && main.TaskbarPopup == first && first.ShowInTaskbar && !second.ShowInTaskbar && first.IsFixed,
                "hiding search keeps one fixed price window as the taskbar representative");
            Chord(first); await Idle();
            Check(main.IsVisible && main.TaskbarPopup is null && !first.ShowInTaskbar && !second.ShowInTaskbar,
                "restoring search returns taskbar ownership without adding duplicate entries");
            HideSearch(main); first.Close(); await Idle();
            Check(main.TaskbarPopup == second && second.ShowInTaskbar && !main.IsVisible,
                "closing the representative price transfers the taskbar entry to another open quote");
            Check(missingEntries == 0,
                "hide, restore and representative close never leave the application without a visible taskbar entry");
            main.PropertyChanged -= ObserveTaskbar;
            first.PropertyChanged -= ObserveTaskbar;
            second.PropertyChanged -= ObserveTaskbar;
        }
        finally { main.Close(); }
    }
    private static async Task GroupCloseTests()
    {
        foreach (var rectangle in new[] { false, true })
        {
            var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
            try
            {
                var first = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
                var second = (await main.OpenStockAsync(DemoMarketData.SampleStocks[1]))!;
                var outside = (await main.OpenStockAsync(DemoMarketData.SampleStocks[2]))!;
                first.Position = new(100, 160); second.Position = new(100, 220); outside.Position = new(700, 400);
                await Idle();
                if (rectangle)
                {
                    var a = PopupSelection.WindowBounds(first); var b = PopupSelection.WindowBounds(second);
                    main.Selection.DesktopDown(new(a.X - 10, a.Y - 10), false);
                    main.Selection.DesktopUp(new(Math.Max(a.Right, b.Right) + 10, b.Bottom + 10));
                }
                else { ControlClick(first); ControlClick(second); }
                first.SetFixed(true);
                RightClick(first, 1); Check(first.IsVisible && second.IsVisible && !first.IsFixed && !second.IsFixed,
                    "the first right click still unpins the selected group");
                first.ProcessMouseDown(Avalonia.Input.MouseButton.Right, 2);
                Check(first.IsVisible && second.IsVisible, "group double-click waits until the right button is released");
                first.ProcessMouseUp(Avalonia.Input.MouseButton.Right); await Idle();
                Check(!first.IsVisible && !second.IsVisible && outside.IsVisible && main.Popups.Count == 1 && main.SelectedPopups.Count == 0,
                    $"double right click closes the entire {(rectangle ? "rectangle" : "Control-click")} selection and preserves other quotes");
            }
            finally { main.Close(); }
        }
        var hidden = MakeMain(); await hidden.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        var one = (await hidden.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
        var two = (await hidden.OpenStockAsync(DemoMarketData.SampleStocks[1]))!;
        ControlClick(one); ControlClick(two); HideSearch(hidden); var ended = false; hidden.SessionEnded += (_, _) => ended = true;
        RightClick(one, 1); RightClick(one, 2); await Until(() => ended);
        Check(hidden.Popups.Count == 0 && !hidden.IsVisible && !one.IsVisible && !two.IsVisible,
            "closing the final selected group ends a hidden-search application cleanly");
    }
}
