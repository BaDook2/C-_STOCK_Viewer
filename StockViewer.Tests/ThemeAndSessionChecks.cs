using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static Color BrushColor(IBrush? brush) => ((ISolidColorBrush)brush!).Color;
    private static Color ThemeColor(Control control, string key) => BrushColor((IBrush)control.FindResource(control.ActualThemeVariant, key)!);
    private static async Task ThemeAndSessionTests()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "StockViewer-theme-" + Guid.NewGuid().ToString("N")));
        Check(!store.LoadDarkMode(), "new installations default to light mode");
        var main = MakeMain(store);
        await main.UseServiceAsync(new DemoMarketData(), DemoMarketData.SampleStocks);
        var domestic = (await main.OpenStockAsync(DemoMarketData.SampleStocks[0]))!;
        var apple = new QuoteWindow(new("US:AAPL.O", "애플", "NASDAQ", "AAPL"), true);
        apple.Show();
        var now = DateTimeOffset.UtcNow;
        apple.SetConnection(new("connected", true));
        apple.ApplyQuote(new("US:AAPL.O", 100, 2, now, SourceTime: now, MarketStatus: "OPEN", Currency: "USD"));
        try
        {
            await Idle();
            Check(Find<Button>(main, "MinimizeButton").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any()
                && Find<Button>(main, "CloseButton").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(),
                "window buttons render vector strokes without a Windows icon font");
            SaveImage(main, "search-light.png"); SaveImage(apple, "quote-open-light.png");
            var toggle = Find<Button>(main, "ThemeButton");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(main.ActualThemeVariant == ThemeVariant.Dark && domestic.ActualThemeVariant == ThemeVariant.Dark
                && apple.ActualThemeVariant == ThemeVariant.Dark, "dark mode immediately reaches all already-open price windows");
            Check(store.LoadDarkMode() && Find<Canvas>(main, "SunIcon").IsVisible, "dark mode is persisted and the toggle offers the sun icon");
            Check(BrushColor(Find<Border>(apple, "Card").Background) == Color.Parse("#1F2633")
                && BrushColor(Find<TextBlock>(apple, "PriceText").Foreground) == ThemeColor(apple, "RedBrush"),
                "an already-open popup changes both its surface and active price palette");
            SaveImage(main, "search-dark.png"); SaveImage(apple, "quote-open-dark.png");
            apple.ApplyQuote(new("US:AAPL.O", 100, 2, now.AddSeconds(1), SourceTime: now, MarketStatus: "CLOSE", Currency: "USD"));
            Check(apple.IsMarketClosed && new[] { "StockName", "PriceText", "PercentText" }.All(name =>
                BrushColor(Find<TextBlock>(apple, name).Foreground) == ThemeColor(apple, "TextMutedBrush")),
                "a full market close greys the name, price and percentage even without a new trade");
            SaveImage(apple, "quote-closed-dark.png");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(!store.LoadDarkMode() && main.ActualThemeVariant == ThemeVariant.Light && apple.ActualThemeVariant == ThemeVariant.Light
                && BrushColor(Find<Border>(apple, "Card").Background) == Colors.White,
                "the second toggle restores light mode in existing windows and settings");
            Check(BrushColor(Find<TextBlock>(apple, "PriceText").Foreground) == ThemeColor(apple, "TextMutedBrush"),
                "changing the theme does not restore trading colours to a closed market");
            SaveImage(apple, "quote-closed-light.png");
            apple.ApplyQuote(new("US:AAPL.O", 99, -1, now.AddSeconds(2), SourceTime: now.AddSeconds(-1), MarketStatus: "OPEN", Currency: "USD"));
            Check(!apple.IsMarketClosed && apple.DisplayedPrice == "$100.00"
                && BrushColor(Find<TextBlock>(apple, "PriceText").Foreground) == ThemeColor(apple, "RedBrush"),
                "reopening restores the retained price colour without accepting an older trade");
            apple.ApplyQuote(new("US:AAPL.O", 100, 2, now.AddSeconds(1.5), SourceTime: now, MarketStatus: "CLOSE", Currency: "USD"));
            Check(!apple.IsMarketClosed, "a delayed equal-time trade cannot undo the latest session reopening");
            apple.ApplyQuote(new("US:AAPL.O", 99, -1, now.AddSeconds(3), SourceTime: now.AddSeconds(1), MarketStatus: "OPEN", Currency: "USD"));
            Check(BrushColor(Find<TextBlock>(apple, "PriceText").Foreground) == ThemeColor(apple, "BlueBrush"),
                "a new falling quote returns to blue after reopening");
            apple.ApplyQuote(new("US:AAPL.O", 99, -1, now.AddSeconds(4), SourceTime: now.AddSeconds(1), MarketStatus: "CLOSE", Currency: "USD"));
            apple.ApplyQuote(new("US:AAPL.O", 1, 1, now.AddSeconds(3), SourceTime: now, MarketStatus: "OPEN", Currency: "USD"));
            Check(apple.IsMarketClosed && apple.DisplayedPrice == "$99.00", "an out-of-order response cannot reopen a closed market or roll its price back");
        }
        finally
        {
            apple.Close(); domestic.Close(); main.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static JsonElement DomesticSessionFrame(object? extended = null, string status = "CLOSE", string time = "2026-11-09T15:30:00+09:00")
        => Json(JsonSerializer.Serialize(new
        {
            datas = new[] { new {
                itemCode = "005930", closePrice = "10,000", fluctuationsRatio = "0.00", compareToPreviousClosePrice = "0",
                marketStatus = status, localTradedAt = time, marketSessionType = "regularMarket", currencyType = new { code = "KRW" }, overMarketPriceInfo = extended
            } }
        }));
    private static void ExtendedSessionTests()
    {
        var krxAfter = DomesticSessionFrame(null, "OPEN", "2026-11-09T18:00:00+09:00").GetRawText()
            .Replace("regularMarket", "afterMarket");
        var krx = PublicMarketData.ParseQuotes(Json(krxAfter)).Single();
        Check(krx is { Price: 10000, MarketStatus: "OPEN" } && krx.Source.Contains("KRX") && krx.Source.Contains("애프터마켓"),
            "KRX after-market quotes remain active without an NXT feed");
        var extended = new { tradingSessionType = "PRE_MARKET", overMarketStatus = "OPEN", overPrice = "10,200",
            fluctuationsRatio = "2.00", compareToPreviousPrice = new { code = "2" }, localTradedAt = "2026-11-10T08:20:00+09:00" };
        var pre = PublicMarketData.ParseQuotes(DomesticSessionFrame(extended)).Single();
        Check(pre.Price == 10200 && pre.ChangePercent == 2 && pre.MarketStatus == "OPEN" && pre.Source.Contains("NXT · 프리마켓"),
            "Korean NXT premarket overrides yesterday's KRX close");
        var after = extended with { tradingSessionType = "AFTER_MARKET", overPrice = "9,800", fluctuationsRatio = "2.00",
            compareToPreviousPrice = new { code = "5" }, localTradedAt = "2026-11-09T18:30:00+09:00" };
        var post = PublicMarketData.ParseQuotes(DomesticSessionFrame(after)).Single();
        Check(post.Price == 9800 && post.ChangePercent == -2 && post.MarketStatus == "OPEN" && post.Source.Contains("애프터마켓"),
            "Korean NXT after-hours preserves the signed previous-day return");
        Check(PublicMarketData.ParseQuotes(DomesticSessionFrame(after with { overMarketStatus = "CLOSE" })).Single()
            is { Price: 9800, MarketStatus: "CLOSE" }, "a completed NXT session retains its last price and reports the full close");
        Check(PublicMarketData.ParseQuotes(DomesticSessionFrame(after with { overPrice = "bad" })).Single()
            is { Price: 10000, MarketStatus: "OPEN" }, "an open extended market is not greyed merely because its first price is unavailable");
        Check(PublicMarketData.ParseQuotes(DomesticSessionFrame(after with { localTradedAt = "2026-11-09T14:00:00+09:00" })).Single().Price == 10000,
            "older NXT trades cannot replace a newer KRX price");
        Check(PublicMarketData.ParseQuotes(DomesticSessionFrame(after, "OPEN", "2026-11-09T18:31:00+09:00")).Single()
            is { Price: 10000, MarketStatus: "OPEN" }, "the latest regular trade wins and an open regular market stays active");
        Check(PublicMarketData.ParseQuotes(DomesticSessionFrame(after with { tradingSessionType = "UNKNOWN" })).Single()
            is { Price: 10000, MarketStatus: "CLOSE" }, "unknown extended sessions do not invent a price or an open market");
        var alternate = DomesticSessionFrame(extended).GetRawText().Replace("overMarketPriceInfo", "nxtOverMarketPriceInfo");
        Check(PublicMarketData.ParseQuotes(Json(alternate)).Single().Price == 10200, "the alternate NXT field is supported too");
        var us = new { tradingSessionType = "AFTER_MARKET", overPrice = "96.00", localTradedAt = "2026-10-30T16:00:00-04:00", overMarketStatus = "OPEN" };
        Check(PublicMarketData.ParseQuotes(UsQuoteFrame(us), true).Single() is { Price: 96, MarketStatus: "OPEN" },
            "US extended trading starts even before the first timestamp advances past the close");
        Check(PublicMarketData.ParseQuotes(UsQuoteFrame(us with { overPrice = "bad" }), true).Single()
            is { Price: 95, MarketStatus: "OPEN" }, "US session status is updated independently of price parsing");
        Check(PublicMarketData.ParseQuotes(UsQuoteFrame(us with { localTradedAt = "2026-10-30T20:00:00-04:00", overMarketStatus = "CLOSE" }), true).Single()
            is { Price: 96, MarketStatus: "CLOSE" }, "US after-hours full close retains its final extended price");
        var preUs = us with { tradingSessionType = "PRE_MARKET", localTradedAt = "2026-11-02T08:00:00-05:00" };
        var noRegularTime = UsQuoteFrame(preUs).GetRawText().Replace("2026-10-30T16:00:00-04:00", "");
        Check(PublicMarketData.ParseQuotes(Json(noRegularTime), true).Single() is { Price: 96, MarketStatus: "OPEN" },
            "a valid US premarket trade can be used when the regular timestamp is missing");
    }

    private static async Task SessionPollingTests()
    {
        var clock = new DayClock { Now = DateTimeOffset.Parse("2026-11-11T07:50:00+09:00") };
        var handler = new DomesticHttpFixture(clock);
        await using var service = new PublicMarketData(new PublicHttpFixture(), domesticHandler: handler, domesticClock: clock);
        Quote? latest = null; service.QuoteReceived += quote => Volatile.Write(ref latest, quote);
        await service.SetSubscriptionsAsync(["005930"]); service.Start();
        await Until(() => Volatile.Read(ref latest)?.MarketStatus == "CLOSE");
        clock.Now = DateTimeOffset.Parse("2026-11-11T18:00:00+09:00");
        await Until(() => Volatile.Read(ref latest) is { Price: 10200, MarketStatus: "OPEN" });
        Check(true, "Toss polling resumes automatically in domestic extended trading");
        clock.Now = DateTimeOffset.Parse("2026-11-11T20:00:00+09:00");
        await Until(() => Volatile.Read(ref latest) is { Price: 10200, MarketStatus: "CLOSE" });
        Check(true, "Toss polling reports a full close without discarding the last price");
        clock.Now = DateTimeOffset.Parse("2026-11-12T08:10:00+09:00");
        await Until(() => Volatile.Read(ref latest)?.MarketStatus == "OPEN");
        Check(true, "Toss NXT premarket automatically reopens the existing domestic popup");
    }
}
