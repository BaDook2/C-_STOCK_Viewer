using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static RawInputModifiers Held(MouseButton button) => button == MouseButton.Left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton;
    private static async Task PointerPipelineTests()
    {
        foreach (var first in new[] { MouseButton.Left, MouseButton.Right })
        foreach (var releaseFirst in new[] { MouseButton.Left, MouseButton.Right })
        {
            var popup = new QuoteWindow(DemoMarketData.SampleStocks[0], true) { Position = new(500, 300) };
            popup.Show(); await Idle();
            var requests = 0; IPointer? pointer = null; var releasedBeforeRecall = false;
            popup.SearchRequested += (_, _) => requests++;
            popup.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel, true);
            popup.AddHandler(InputElement.PointerReleasedEvent, (_, _) => releasedBeforeRecall = requests == 0 && popup.IsVisible,
                RoutingStrategies.Tunnel, true);
            var point = new Point(30, 16);
            var second = first == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
            var releaseLast = releaseFirst == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
            popup.MouseDown(point, first);
            popup.MouseDown(point, second, Held(first));
            Check(pointer?.Captured == popup && !popup.IsFixed, "real Avalonia multi-button input captures the quote and gives the chord priority");
            popup.MouseUp(point, releaseFirst, Held(releaseLast)); await Idle();
            Check(pointer?.Captured == popup && requests == 0 && popup.IsVisible, "partial release arrives as movement and keeps the quote capture");
            popup.MouseUp(point, releaseLast);
            Check(releasedBeforeRecall && popup.IsVisible, "raw input's last button up is consumed before recall"); await Idle();
            Check(requests == 1 && pointer?.Captured is null && !popup.IsFixed, "raw input chord recalls exactly once and releases capture");
            popup.MouseDown(point, MouseButton.Right); popup.MouseUp(point, MouseButton.Right); await Idle();
            Check(popup.IsVisible && popup.IsFixed, "real right-click Fix works after a chord");
            popup.MouseDown(point, MouseButton.Right);
            Check(popup.IsVisible && pointer?.Captured == popup, "real second right down keeps HWND and pointer capture until up");
            popup.MouseUp(point, MouseButton.Right); await Idle();
            Check(!popup.IsVisible && pointer?.Captured is null, "real right double-click closes only after up and releases capture");
        }
        var main = MakeMain(); await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks); await Idle();
        var box = Find<TextBox>(main, "SearchBox"); box.Focus();
        main.KeyTextInput("큐리언트"); await Idle();
        Check(box.Text == "큐리언트", "Avalonia text input accepts more than two Korean characters");
        box.Text = "삼성"; await Idle();
        Check(Find<Avalonia.Controls.Primitives.Popup>(main, "Suggestions").IsOpen, "explicit text input opens the custom autocomplete popup");
        var suggestions = Find<Avalonia.Controls.Primitives.Popup>(main, "Suggestions");
        Check(ReferenceEquals(suggestions.PlacementTarget, box), "autocomplete is anchored to the search textbox");
        SaveImage(suggestions.Child!, "search-dropdown.png");
        var results = Find<ListBox>(main, "SearchResults");
        var row = (Control)results.ContainerFromIndex(0)!;
        var host = TopLevel.GetTopLevel(row)!;
        var rowPoint = row.TranslatePoint(new Point(10, row.Bounds.Height / 2), host)!.Value;
        host.MouseDown(rowPoint, MouseButton.Left); host.MouseUp(rowPoint, MouseButton.Left);
        await Until(() => main.Popups.Count == 1);
        Check(main.Popups.ContainsKey("005930"), "an actual pointer click on an autocomplete row opens its stock");
        var close = Find<Button>(main, "CloseButton");
        var closePoint = close.TranslatePoint(new Point(12, 11), main)!.Value;
        var popupStock = main.Popups.Values.Single(); await Idle();
        main.Activate(); box.Focus(); box.Text = "삼성전자"; await Idle();
        main.MouseDown(closePoint, MouseButton.Left); main.MouseUp(closePoint, MouseButton.Left); await Idle();
        Check(!main.IsVisible && popupStock.IsVisible, "first actual X click hides search even when autocomplete is open");
        popupStock.Close(); await Idle();
    }
}
