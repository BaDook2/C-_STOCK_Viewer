using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

namespace StockViewer.Core;

internal sealed record DiagnosticLogOptions(long FileBytes = 5 * 1024 * 1024, int Files = 10, TimeSpan? Retention = null);

/// <summary>Local, bounded diagnostics. Producers never wait for filesystem I/O.</summary>
public sealed class DiagnosticLog : IDisposable
{
    private static DiagnosticLog? current;
    private static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Channel<Entry> entries = Channel.CreateBounded<Entry>(new BoundedChannelOptions(1024)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, bool Failed)> requests = new();
    private readonly DiagnosticLogOptions options;
    private readonly Task writer;
    private readonly string session = Guid.NewGuid().ToString("N");
    private readonly string prefix = $"stockviewer-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}";
    private long dropped;
    private int disposed, segment;
    private DateTimeOffset stoppedAt;
    private FileStream? stream;
    private DateOnly streamDay;
    public string DirectoryPath { get; }
    public static bool IsEnabled => Volatile.Read(ref current) is not null;
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockViewer", "logs");

    private sealed record Entry(DateTimeOffset Timestamp, string Session, string Event, string Level, object? Data);
    private DiagnosticLog(string directory, DiagnosticLogOptions options)
    { DirectoryPath = directory; this.options = options; writer = Task.Run(WriteAsync); }

    public static DiagnosticLog Start(string? directory = null, string? version = null) => Start(directory ?? DefaultDirectory, version, new());
    internal static DiagnosticLog Start(string directory, string? version, DiagnosticLogOptions options)
    {
        if (options.FileBytes < 1024 || options.Files < 1 || options.Retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        var log = new DiagnosticLog(directory, options);
        if (Interlocked.CompareExchange(ref current, log, null) is not null)
        { log.Dispose(); throw new InvalidOperationException("진단 로그가 이미 시작되었습니다."); }
        Write("session.started", new { schema = 1, version, processId = Environment.ProcessId, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), framework = RuntimeInformation.FrameworkDescription,
            timeZone = TimeZoneInfo.Local.Id, utcOffset = DateTimeOffset.Now.Offset.ToString() });
        return log;
    }
    public static void Write(string name, object? data = null, string level = "info") => Volatile.Read(ref current)?.Enqueue(name, data, level);
    private void Enqueue(string name, object? data, string level)
    { if (!entries.Writer.TryWrite(new(DateTimeOffset.Now, session, name, level, data))) Interlocked.Increment(ref dropped); }

    // Only structured error identifiers: exception messages/stacks can contain URLs,
    // response fragments, cookies or search text and are deliberately not serialized.
    public static object Error(Exception error, bool cancellationRequested = false)
    {
        var socket = error.InnerException as SocketException ?? error.InnerException?.InnerException as SocketException;
        return new { type = error.GetType().Name, kind = error is OperationCanceledException ? cancellationRequested ? "cancelled" : "timeout"
            : error is JsonException or InvalidDataException ? "invalid_data" : error is HttpRequestException ? "network" : "error",
            httpStatus = (int?)(error as HttpRequestException)?.StatusCode,
            requestError = (error as HttpRequestException)?.HttpRequestError.ToString(), socketError = socket?.SocketErrorCode.ToString(),
            innerType = error.InnerException?.GetType().Name, error.HResult };
    }

    internal static RequestTrace Request(string provider, string url) => new(Volatile.Read(ref current), provider, new Uri(url));
    internal sealed class RequestTrace(DiagnosticLog? log, string provider, Uri uri) : IDisposable
    {
        private readonly DateTimeOffset started = DateTimeOffset.Now;
        private readonly long timer = Stopwatch.GetTimestamp();
        private int? status;
        private double? retryAfterSeconds;
        private Exception? failure;
        private bool cancelled;
        public void Response(HttpStatusCode code) => status = (int)code;
        public void RetryAfter(TimeSpan delay) => retryAfterSeconds = delay.TotalSeconds;
        public void Fail(Exception error, bool cancellationRequested) { failure = error; cancelled = cancellationRequested; }
        public void Dispose()
        {
            if (log is null) return;
            // Strip the entire query, headers and body, including anonymous session IDs.
            var endpoint = uri.Host + uri.AbsolutePath;
            var now = DateTimeOffset.Now;
            var key = provider + ":" + endpoint;
            var previous = log.requests.GetValueOrDefault(key);
            var failed = failure is not null;
            log.requests[key] = (now, failed && !cancelled);
            if (!failed && !previous.Failed && previous.At != default && now - previous.At < TimeSpan.FromMinutes(1))
            { log.requests[key] = previous; return; }
            // Bound distinct endpoint bookkeeping as searches/subscriptions change.
            if (log.requests.Count > 2048) log.requests.Clear();
            log.Enqueue("http.completed", new { provider, endpoint, startedAt = started, elapsedMs = Math.Round(Stopwatch.GetElapsedTime(timer).TotalMilliseconds, 1),
                httpStatus = status, retryAfterSeconds, outcome = failed ? cancelled ? "cancelled" : "failed" : "success",
                recovered = !failed && previous.Failed, error = failure is null ? null : Error(failure, cancelled) }, failed && !cancelled ? "warning" : "info");
        }
    }

    private async Task WriteAsync()
    {
        var retryAt = DateTimeOffset.MinValue;
        try
        {
            await foreach (var entry in entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (DateTimeOffset.UtcNow < retryAt) { Interlocked.Increment(ref dropped); continue; }
                long lost = 0;
                try
                {
                    lost = Interlocked.Exchange(ref dropped, 0);
                    if (lost > 0) await AppendAsync(new(DateTimeOffset.Now, session, "log.dropped", "warning", new { count = lost })).ConfigureAwait(false);
                    lost = 0;
                    await AppendAsync(entry).ConfigureAwait(false);
                    if (stream is not null) await stream.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception e) when (IsStorageError(e) || e is JsonException or NotSupportedException or ArgumentException)
                {
                    Interlocked.Add(ref dropped, lost + 1);
                    try { stream?.Dispose(); } catch (Exception closeError) when (IsStorageError(closeError)) { }
                    stream = null; retryAt = DateTimeOffset.UtcNow.AddSeconds(5);
                }
            }
            try
            {
                var lost = Interlocked.Exchange(ref dropped, 0);
                if (lost > 0) await AppendAsync(new(DateTimeOffset.Now, session, "log.dropped", "warning", new { count = lost })).ConfigureAwait(false);
                await AppendAsync(new(stoppedAt, session, "session.stopped", "info", null)).ConfigureAwait(false);
                if (stream is not null) await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (IsStorageError(e) || e is JsonException or NotSupportedException or ArgumentException) { }
        }
        finally { try { stream?.Dispose(); } catch (Exception e) when (IsStorageError(e)) { } }
    }
    private static bool IsStorageError(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private async Task AppendAsync(Entry entry)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, json);
        if (bytes.Length + 1 > Math.Min(64 * 1024, options.FileBytes)) { Interlocked.Increment(ref dropped); return; }
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (stream is null || streamDay != today || stream.Position > 0 && stream.Position + bytes.Length + 1 > options.FileBytes)
        {
            stream?.Dispose(); stream = null;
            Directory.CreateDirectory(DirectoryPath);
            // Leave room for the new segment. Only our own log names are pruned.
            Prune();
            stream = new FileStream(Path.Combine(DirectoryPath, $"{prefix}-{++segment:D4}.jsonl"), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
            streamDay = today;
        }
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.WriteAsync(new byte[] { (byte)'\n' }).ConfigureAwait(false);
    }
    private void Prune()
    {
        var cutoff = DateTime.UtcNow - (options.Retention ?? TimeSpan.FromDays(7));
        var files = new DirectoryInfo(DirectoryPath).GetFiles("stockviewer-*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        foreach (var (file, index) in files.Select((file, index) => (file, index)))
            if (file.LastWriteTimeUtc < cutoff || index >= options.Files - 1)
                try
                {
                    // Another app instance may still be writing this segment.
                    using (new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    file.Delete();
                }
                catch (Exception e) when (IsStorageError(e)) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.CompareExchange(ref current, null, this);
        stoppedAt = DateTimeOffset.Now;
        entries.Writer.TryComplete(); writer.GetAwaiter().GetResult();
    }
}
