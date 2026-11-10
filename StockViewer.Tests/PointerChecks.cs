using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static RawInputModifiers Held(MouseButton button) => button == MouseButton.Left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton;
    private static async Task PointerPipelineTests()
    {
        await HoverAppearanceTests();
        await PointerSelectionTests();
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
    private static async Task HoverAppearanceTests()
    {
        var main = MakeMain(); await Idle();
        try
        {
            foreach (var dark in new[] { false, true })
            {
                Application.Current!.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
                await Idle();
                foreach (var name in new[] { "SearchButton", "SaveLayoutButton", "LoadLayoutButton", "ThemeButton", "MinimizeButton", "CloseButton", "DisplayName", "SearchBox" })
                {
                    main.MouseMove(new Point(-10, -10)); await Idle();
                    var control = Find<Control>(main, name);
                    var foreground = control is Avalonia.Controls.Primitives.TemplatedControl tc ? tc.Foreground : null;
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), main)!.Value;
                    var texts = control.GetVisualDescendants().OfType<TextBlock>().Select(t => (Text: t, Foreground: t.Foreground)).ToArray();
                    main.MouseMove(point); await Idle();
                    Check(control.IsPointerOver && texts.All(t => BrushColor(t.Text.Foreground) == BrushColor(t.Foreground))
                        && (control is not Avalonia.Controls.Primitives.TemplatedControl hovered || BrushColor(hovered.Foreground) == BrushColor(foreground)),
                        $"{name} hover preserves text colours in {(dark ? "dark" : "light")} mode (over={control.IsPointerOver}; before={foreground}; after={(control as Avalonia.Controls.Primitives.TemplatedControl)?.Foreground})");
                    Check(texts.All(t => t.Text.Background is null || t.Text.Background is ISolidColorBrush b && b.Color.A == 0)
                        && control.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                            .All(p => p.Background is null || p.Background is ISolidColorBrush b && b.Color.A == 0),
                        $"{name} hover adds no grey text highlighter in {(dark ? "dark" : "light")} mode");
                    if (name == "SearchButton")
                    {
                        var box = control.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Box");
                        Check(box.Opacity == 1 && BrushColor(box.Background) == ThemeColor(control, "AccentHoverBrush"),
                            "search hover changes its outer background while keeping content fully opaque");
                        SaveImage(main, dark ? "search-hover-dark.png" : "search-hover-light.png");
                    }
                }
            }
        }
        finally { main.Close(); Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; }
    }
}
