using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

internal sealed class FakeBybitPrivateConnection(Channel<string> messages) : IBybitPrivateConnection
{
    public Channel<string> Messages { get; } = messages;

    public bool IsDisposed { get; private set; }

    async IAsyncEnumerable<string> IBybitPrivateConnection.Messages([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (string message in Messages.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;

        return ValueTask.CompletedTask;
    }
}