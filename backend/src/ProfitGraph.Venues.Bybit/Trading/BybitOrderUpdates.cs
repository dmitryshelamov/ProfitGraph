using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     The order updates Bybit lookups produce, by bot, for the bot's runner.
/// </summary>
internal sealed class BybitOrderUpdates
{
    private readonly ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>> _channels = new ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>>();

    public void Publish(BotId botId, IEnumerable<ExternalOrderUpdate> updates)
    {
        Channel<ExternalOrderUpdate> channel = Channel(botId);

        foreach (ExternalOrderUpdate update in updates)
        {
            channel.Writer.TryWrite(update);
        }
    }

    public async IAsyncEnumerable<ExternalOrderUpdate> Read(BotId botId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (ExternalOrderUpdate update in Channel(botId).Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }
    }

    private Channel<ExternalOrderUpdate> Channel(BotId botId)
    {
        return _channels.GetOrAdd(botId, _ => System.Threading.Channels.Channel.CreateUnbounded<ExternalOrderUpdate>());
    }
}
