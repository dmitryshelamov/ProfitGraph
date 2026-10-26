using System.Threading.Channels;

using Grpc.Core;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     One bidirectional stream of the mock Invest API: the requests the client wrote, and responses the test pushes, fails or
///     ends by hand.
/// </summary>
internal sealed class MockTInvestDuplexCall<TRequest, TResponse> : IClientStreamWriter<TRequest>, IAsyncStreamReader<TResponse>, IDisposable
{
    private readonly Channel<TResponse> _responses = Channel.CreateUnbounded<TResponse>();

    public List<TRequest> Requests { get; } = [];

    public Metadata Headers { get; init; } = [];

    public bool Disposed { get; private set; }

    public TResponse Current { get; private set; } = default!;

    public WriteOptions? WriteOptions { get; set; }

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (!await _responses.Reader.WaitToReadAsync(cancellationToken))
        {
            return false;
        }

        Current = await _responses.Reader.ReadAsync(cancellationToken);

        return true;
    }

    public Task WriteAsync(TRequest message)
    {
        lock (Requests)
        {
            Requests.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task WriteAsync(TRequest message, CancellationToken cancellationToken)
    {
        return WriteAsync(message);
    }

    public Task CompleteAsync()
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Disposed = true;
        _responses.Writer.TryComplete();
    }

    public void Push(TResponse response)
    {
        _responses.Writer.TryWrite(response);
    }

    public void Fail(RpcException error)
    {
        _responses.Writer.TryComplete(error);
    }

    public void End()
    {
        _responses.Writer.TryComplete();
    }
}