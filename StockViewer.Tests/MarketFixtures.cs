using System.Collections.Concurrent;
using StockViewer.Core;
namespace StockViewer.Tests;
internal static partial class Program
{
    private sealed class DelayedQuoteService : IMarketDataService, IStockSearchService
    {
        public IReadOnlyList<Stock> SearchResults { get; init; } = [];
        public bool IsDemo => true;
        public bool Disposed { get; private set; }
        public bool SnapshotRequested { get; private set; }
        public CancellationToken SnapshotCancellation { get; private set; }
        public TaskCompletionSource<Quote> Snapshot { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event Action<Quote>? QuoteReceived;
        public event Action<ConnectionStatus>? StatusChanged;
        public void Start() => StatusChanged?.Invoke(new("fixture", true, true));
        public void Publish(Quote quote) => QuoteReceived?.Invoke(quote);
        public Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
            => Task.FromResult<IReadOnlyList<Stock>>(DemoMarketData.SampleStocks);
        public Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
        { SnapshotRequested = true; SnapshotCancellation = cancellation; return Snapshot.Task.WaitAsync(cancellation); }
        public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Stock>> SearchAsync(string query, CancellationToken cancellation = default)
            => Task.FromResult(SearchResults);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class DelayedSearchService : IMarketDataService, IStockSearchService
    {
        public List<(string Query, TaskCompletionSource<IReadOnlyList<Stock>> Reply)> Requests { get; } = [];
        public bool IsDemo => true;
        public event Action<Quote>? QuoteReceived { add { } remove { } }
        public event Action<ConnectionStatus>? StatusChanged;
        public void Start() => StatusChanged?.Invoke(new("fixture", true, true));
        public Task<IReadOnlyList<Stock>> GetStocksAsync(CancellationToken cancellation = default)
            => Task.FromResult<IReadOnlyList<Stock>>(DemoMarketData.SampleStocks);
        public Task<Quote> GetSnapshotAsync(string code, CancellationToken cancellation = default)
            => Task.FromResult(new Quote(code, 72500, 1.25m, DateTimeOffset.UtcNow, IsDemo: true));
        public Task SetSubscriptionsAsync(IEnumerable<string> codes, CancellationToken cancellation = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Stock>> SearchAsync(string query, CancellationToken cancellation = default)
        {
            // Ignore cancellation to exercise replies already in flight.
            var reply = new TaskCompletionSource<IReadOnlyList<Stock>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((query, reply)); return reply.Task;
        }
        public ValueTask DisposeAsync()
        {
            foreach (var request in Requests) request.Reply.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }

}
