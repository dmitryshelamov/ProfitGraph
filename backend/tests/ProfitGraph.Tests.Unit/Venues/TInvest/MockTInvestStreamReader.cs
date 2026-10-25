using Grpc.Core;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     A server stream of the mock Invest API over an asynchronous sequence.
/// </summary>
internal sealed class MockTInvestStreamReader<T>(IAsyncEnumerable<T> messages, CancellationToken cancellationToken) : IAsyncStreamReader<T>, IDisposable
{
    private readonly IAsyncEnumerator<T> _messages = messages.GetAsyncEnumerator(cancellationToken);

    public T Current => _messages.Current;

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        return await _messages.MoveNextAsync();
    }

    public void Dispose()
    {
        _ = _messages.DisposeAsync().AsTask();
    }
}