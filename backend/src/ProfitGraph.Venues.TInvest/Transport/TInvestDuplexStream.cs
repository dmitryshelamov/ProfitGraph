using System.Runtime.CompilerServices;

using Grpc.Core;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     An open bidirectional Invest API stream: every request written is admitted under the token's service-group limit first,
///     and a failure while reading is a <see cref="TInvestRpcException" />. Writes are serialized because gRPC allows one at a time.
/// </summary>
internal sealed class TInvestDuplexStream<TRequest, TResponse>(
    AsyncDuplexStreamingCall<TRequest, TResponse> call,
    Func<CancellationToken, Task> admit,
    CancellationToken cancellationToken) : IDisposable
{
    private readonly SemaphoreSlim _writing = new SemaphoreSlim(initialCount: 1, maxCount: 1);

    public void Dispose()
    {
        call.Dispose();
    }

    public async Task Send(TRequest request, CancellationToken token)
    {
        await _writing.WaitAsync(token);

        try
        {
            await admit(token);
            await call.RequestStream.WriteAsync(request, token);
        }
        catch (RpcException error) when (!token.IsCancellationRequested)
        {
            throw TInvestRpcException.From(error);
        }
        finally
        {
            _writing.Release();
        }
    }

    public async IAsyncEnumerable<TResponse> Read([EnumeratorCancellation] CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);

        while (true)
        {
            bool next;

            try
            {
                next = await call.ResponseStream.MoveNext(linked.Token);
            }
            catch (RpcException error) when (!linked.IsCancellationRequested)
            {
                throw TInvestRpcException.From(error);
            }

            if (!next)
            {
                yield break;
            }

            yield return call.ResponseStream.Current;
        }
    }
}