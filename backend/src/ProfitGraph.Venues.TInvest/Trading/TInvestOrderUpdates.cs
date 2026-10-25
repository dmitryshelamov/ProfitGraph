using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The order updates T-Invest lookups and the order state stream produce, by bot, for the bot's runner.
/// </summary>
internal sealed class TInvestOrderUpdates
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

    /// <summary>
    ///     Starts the bot's updates afresh; what an earlier connection published is reconciled again.
    /// </summary>
    public void Reset(BotId botId)
    {
        _channels[botId] = System.Threading.Channels.Channel.CreateUnbounded<ExternalOrderUpdate>();
    }

    /// <summary>
    ///     Ends the bot's updates with the error, so its reader fails with it.
    /// </summary>
    public void Fail(BotId botId, Exception exception)
    {
        Channel(botId).Writer.TryComplete(exception);
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