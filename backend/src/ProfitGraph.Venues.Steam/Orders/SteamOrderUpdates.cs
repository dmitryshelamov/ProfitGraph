using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     The order updates the Steam journal produces, by bot, for the bot's runner.
/// </summary>
internal sealed class SteamOrderUpdates
{
    private readonly ConcurrentDictionary<Guid, Channel<ExternalOrderUpdate>> _channels = new ConcurrentDictionary<Guid, Channel<ExternalOrderUpdate>>();

    public void Publish(Guid botId, ExternalOrderUpdate update)
    {
        Channel(botId).Writer.TryWrite(update);
    }

    public async IAsyncEnumerable<ExternalOrderUpdate> Read(Guid botId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (ExternalOrderUpdate update in Channel(botId).Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }
    }

    private Channel<ExternalOrderUpdate> Channel(Guid botId)
    {
        return _channels.GetOrAdd(botId, _ => System.Threading.Channels.Channel.CreateUnbounded<ExternalOrderUpdate>());
    }
}