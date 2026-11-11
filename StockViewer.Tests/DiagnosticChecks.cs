using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Avalonia;
using StockViewer.Core;
using StockViewer.Desktop;

namespace StockViewer.Tests;

internal static partial class Program
{
    private static JsonElement[] ReadDiagnostics(string directory) => Directory.GetFiles(directory, "stockviewer-*.jsonl")
        .SelectMany(File.ReadAllLines).Select(Json).ToArray();
    private static bool IsEvent(JsonElement entry, string name) => entry.GetProperty("event").GetString() == name;

    private static async Task DiagnosticTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "StockViewer-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var concurrentPath = Path.Combine(root, "concurrent");
            using (DiagnosticLog.Start(concurrentPath, "test-version"))
                await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
                { for (var i = 0; i < 100; i++) DiagnosticLog.Write("test.concurrent", new { worker, i }); })));
            var entries = ReadDiagnostics(concurrentPath);
            Check(entries.Count(e => IsEvent(e, "test.concurrent")) == 400
                && entries.Where(e => IsEvent(e, "test.concurrent")).Select(e => e.GetProperty("data").ToString()).Distinct().Count() == 400,
                "all concurrent diagnostic records survive shutdown without mixed or partial JSON lines");
            Check(entries.Count(e => IsEvent(e, "session.started")) == 1 && entries.Count(e => IsEvent(e, "session.stopped")) == 1
                && entries.All(e => DateTimeOffset.TryParse(e.GetProperty("timestamp").GetString(), out _))
                && entries.Select(e => e.GetProperty("session").GetString()).Distinct().Count() == 1,
                "logs identify the app session and include parseable timestamps with UTC offsets");

            var burstPath = Path.Combine(root, "burst");
            using (DiagnosticLog.Start(burstPath))
            {
                for (var i = 0; i < 5000; i++) DiagnosticLog.Write("test.burst", new { i });
                DiagnosticLog.Write("test.oversized", new { text = new string('x', 100_000) });
            }
            var burst = ReadDiagnostics(burstPath);
            Check(burst.Count(e => IsEvent(e, "test.burst")) + burst.Where(e => IsEvent(e, "log.dropped"))
                .Sum(e => e.GetProperty("data").GetProperty("count").GetInt64()) == 5001
                && burst.Count(e => IsEvent(e, "session.stopped")) == 1,
                "burst overflow and oversized records are accounted for without losing the shutdown event");

            var rotationPath = Path.Combine(root, "rotation"); Directory.CreateDirectory(rotationPath);
            var old = Path.Combine(rotationPath, "stockviewer-old.jsonl"); File.WriteAllText(old, "old");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
            var other = Path.Combine(rotationPath, "leave-me.txt"); File.WriteAllText(other, "user file");
            using (DiagnosticLog.Start(rotationPath, "test", new DiagnosticLogOptions(FileBytes: 2048, Files: 3)))
                for (var i = 0; i < 80; i++) DiagnosticLog.Write("test.rotation", new { i, text = new string('x', 400) });
            var rotated = Directory.GetFiles(rotationPath, "stockviewer-*.jsonl");
            Check(rotated.Length <= 3 && rotated.All(f => new FileInfo(f).Length <= 2048) && !File.Exists(old) && File.Exists(other),
                "retention removes expired and excess log segments while respecting file size and unrelated files");
            Check(ReadDiagnostics(rotationPath).Any(e => IsEvent(e, "test.rotation") && e.GetProperty("data").GetProperty("i").GetInt32() == 79)
                && ReadDiagnostics(rotationPath).Any(e => IsEvent(e, "session.stopped")),
                "rotation preserves the newest diagnostic events and shutdown record");

            var blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "keep");
            using (DiagnosticLog.Start(Path.Combine(blocked, "logs")))
            {
                await using var service = new PublicMarketData(new PublicHttpFixture());
                Check((await service.GetSnapshotAsync("005930")).Price > 0,
                    "an unwritable log directory never interrupts real quote parsing or delivery");
            }
            Check(File.ReadAllText(blocked) == "keep", "log storage failures leave the blocking user file intact");
            await DiagnosticHttpTests(Path.Combine(root, "http"));
            await DiagnosticRecoveryTests(Path.Combine(root, "recovery"));
            await DiagnosticPopupTests(Path.Combine(root, "popup"));
            var deadlinePath = Path.Combine(root, "deadline");
            using (DiagnosticLog.Start(deadlinePath))
            {
                var main = MakeMain(); var slow = new ControlledQuotes();
                try
                {
                    var stock = new Stock("005930", "[로그 검증용] 시세 미수신", "KOSPI");
                    await main.UseServiceAsync(slow, [stock]);
                    var pending = main.OpenStockAsync(stock);
                    await Until(() => slow.Requests.Count == 1);
                    slow.Requests[0].Reply.TrySetException(new IOException("DO_NOT_LOG initial error"));
                    Check(await pending is null && main.DataDelayNotice is not null,
                        "first-price diagnostics preserve the existing three-second notice behavior");
                }
                finally { main.Close(); await Idle(); }
            }
            var deadlines = ReadDiagnostics(deadlinePath);
            Check(deadlines.Any(e => IsEvent(e, "popup.initial_request_failed")) && deadlines.Any(e => IsEvent(e, "popup.first_quote_timeout")
                && e.GetProperty("data").GetProperty("deadlineSeconds").GetInt32() == 3),
                "snapshot failure and the three-second first-price deadline are both persisted");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class DiagnosticHttpFixture : HttpMessageHandler
    {
        public string Mode = "success";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Mode == "timeout") throw new TaskCanceledException("DO_NOT_LOG timeout", new TimeoutException("DO_NOT_LOG"));
            if (Mode == "network") throw new HttpRequestException("DO_NOT_LOG network");
            var response = new HttpResponseMessage(Mode == "limited" ? HttpStatusCode.TooManyRequests
                : Mode == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            if (Mode == "limited") response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30));
            response.Headers.Add("Set-Cookie", "DO_NOT_LOG=cookie");
            response.Content = new StringContent(Mode == "invalid" ? "DO_NOT_LOG malformed JSON" : "{\"result\":{\"private\":\"DO_NOT_LOG\"}}");
            return Task.FromResult(response);
        }
    }
    private static async Task DiagnosticHttpTests(string path)
    {
        using (DiagnosticLog.Start(path))
        {
            var handler = new DiagnosticHttpFixture(); var clock = new DayClock { Now = DateTimeOffset.UtcNow };
            using var session = new TossPublicSession(handler, clock, "toss.domestic");
            const string url = "https://wts-info-api.tossinvest.com/api/v5/stock-prices?token=DO_NOT_LOG";
            await session.SendAsync(url, default, "DO_NOT_LOG body"); await session.SendAsync(url, default);
            foreach (var mode in new[] { "timeout", "network", "invalid", "unavailable", "limited" })
            {
                handler.Mode = mode;
                try { await session.SendAsync(url, default); throw new Exception("Expected fixture failure"); }
                catch (Exception e) when (e is IOException or HttpRequestException or JsonException or OperationCanceledException) { }
            }
            handler.Mode = "success"; clock.Now = clock.Now.AddSeconds(31);
            await session.SendAsync(url, default);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Throws<OperationCanceledException>(() => session.SendAsync(url, cancellation.Token), "caller cancellation still propagates with diagnostics enabled");
            await using var naver = new PublicMarketData(new PublicHttpFixture { RateLimited = true });
            await Throws<IOException>(() => naver.GetSnapshotAsync("US:AAPL.O"), "Naver rate limiting still propagates with diagnostics enabled");
        }
        var entries = ReadDiagnostics(path); var requests = entries.Where(e => IsEvent(e, "http.completed")).Select(e => e.GetProperty("data")).ToArray();
        Check(requests.Any(e => e.GetProperty("httpStatus").ValueKind == JsonValueKind.Number && e.GetProperty("httpStatus").GetInt32() == 503)
            && requests.Any(e => e.GetProperty("httpStatus").ValueKind == JsonValueKind.Number && e.GetProperty("httpStatus").GetInt32() == 429
                && e.GetProperty("retryAfterSeconds").GetDouble() == 30),
            "HTTP failures record provider status and Retry-After rather than only a generic disconnection");
        Check(requests.Any(e => e.GetProperty("error").ValueKind == JsonValueKind.Object && e.GetProperty("error").GetProperty("kind").GetString() == "timeout")
            && requests.Any(e => e.GetProperty("error").ValueKind == JsonValueKind.Object && e.GetProperty("error").GetProperty("kind").GetString() == "invalid_data")
            && requests.Any(e => e.GetProperty("outcome").GetString() == "cancelled"),
            "timeouts, invalid responses and deliberate cancellations are distinguishable in the log");
        Check(requests.Any(e => e.GetProperty("provider").GetString() == "naver")
            && requests.Count(e => e.GetProperty("provider").GetString() == "toss.domestic" && e.GetProperty("outcome").GetString() == "success") == 2
            && requests.Any(e => e.GetProperty("recovered").GetBoolean()),
            "both providers are logged, repeated healthy requests are sampled, and recovery bypasses sampling");
        Check(requests.All(e => e.GetProperty("elapsedMs").GetDouble() >= 0 && DateTimeOffset.TryParse(e.GetProperty("startedAt").GetString(), out _))
            && !string.Join('\n', Directory.GetFiles(path, "*.jsonl").Select(File.ReadAllText)).Contains("DO_NOT_LOG"),
            "request timings persist without query values, headers, cookies, response bodies or exception text");
    }

    private static async Task DiagnosticRecoveryTests(string path)
    {
        using (DiagnosticLog.Start(path))
        {
            var clock = new DayClock { Now = DateTimeOffset.Parse("2026-11-11T18:30:00+09:00") };
            var handler = new DomesticHttpFixture(clock) { FailPrices = true };
            await using var service = new PublicMarketData(new PublicHttpFixture(), domesticHandler: handler, domesticClock: clock);
            var quotes = new ConcurrentDictionary<string, Quote>(); var statuses = new ConcurrentDictionary<string, bool>();
            service.QuoteReceived += quote => quotes[quote.Code] = quote;
            service.StatusChanged += status => { if (status.Code is { } code) statuses[code] = status.Connected; };
            await service.SetSubscriptionsAsync(["005930", "000660"]); service.Start();
            await Until(() => statuses.TryGetValue("005930", out var connected) && !connected);
            handler.FailPrices = false;
            await Until(() => quotes.Count == 2 && statuses.GetValueOrDefault("005930"));
            handler.MissingCode = "005930";
            await Until(() => !statuses.GetValueOrDefault("005930"));
            Check(quotes["005930"].Price == 10200 && statuses.GetValueOrDefault("000660"),
                "diagnostics preserve the last valid price and independent healthy symbol during a missing row");
            handler.MissingCode = null; handler.Price = 10300; clock.Now = clock.Now.AddSeconds(3);
            await Until(() => quotes["005930"].Price == 10300 && statuses.GetValueOrDefault("005930"));
        }
        var entries = ReadDiagnostics(path);
        Check(entries.Any(e => IsEvent(e, "quote.unavailable")) && entries.Any(e => IsEvent(e, "quote.missing"))
            && entries.Count(e => IsEvent(e, "quote.recovered") && e.GetProperty("data").GetProperty("code").GetString() == "005930") == 2,
            "real polling logs separate HTTP failure and missing-row outages and both successful recoveries");
        Check(entries.Where(e => IsEvent(e, "quote.recovered")).All(e => e.GetProperty("data").GetProperty("unavailableMs").GetDouble() >= 0)
            && entries.Any(e => IsEvent(e, "quote.received") && e.GetProperty("data").GetProperty("sourceTime").ValueKind == JsonValueKind.String),
            "recovery includes outage duration and quote diagnostics distinguish source time from receive time");
    }

    private static async Task DiagnosticPopupTests(string path)
    {
        long id;
        using (DiagnosticLog.Start(path))
        {
            var popup = new QuoteWindow(new("005930", "[로그 검증용] 삼성전자", "KOSPI"), true)
            { Position = new(160, 180) };
            try
            {
                id = popup.DiagnosticId;
                popup.ApplyQuote(new("005930", 10000, 1, DateTimeOffset.UtcNow, IsDemo: true));
                popup.Show(); popup.SetFixed(true); await Idle();
                if (native && OperatingSystem.IsMacOS())
                {
                    var handle = popup.TryGetPlatformHandle()!;
                    ObjcSetLevel(handle.HandleDescriptor == "NSView" ? ObjcSend(handle.Handle, ObjcSelector("window")) : handle.Handle, ObjcSelector("setLevel:"), 0);
                    await Until(() => PopupTopmost.Read(popup) == true);
                }
                popup.SetFixed(false);
            }
            finally { popup.Close(); await Idle(); }
        }
        var entries = ReadDiagnostics(path);
        Check(entries.Any(e => IsEvent(e, "popup.opened")) && entries.Any(e => IsEvent(e, "popup.closed"))
            && entries.Count(e => IsEvent(e, "popup.pin_requested")) == 2
            && entries.Where(e => e.GetProperty("event").GetString()!.StartsWith("popup.")).All(e => e.GetProperty("data").GetProperty("popupId").GetInt64() == id),
            "popup lifetime and pin toggles share an identity for correlating with feed failures");
        if (native && OperatingSystem.IsMacOS())
            Check(entries.Any(e => IsEvent(e, "popup.topmost_repair") && e.GetProperty("data").GetProperty("nativeBefore").GetBoolean() == false
                && e.GetProperty("data").GetProperty("nativeAfter").GetBoolean()), "a real native topmost demotion and its repair are saved to disk");
    }
}
