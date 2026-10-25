using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Carries updates that arise in this process, such as a stored submit rejection, to the runner of the bot. A rejection
///     the runner never read is applied again from storage when the bot's engine opens.
/// </summary>
public sealed class BotUpdateBus
{
    private readonly ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>> _channels = new ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>>();

    public void Publish(BotId botId, ExternalOrderUpdate update)
    {
        Channel(botId).Writer.TryWrite(update);
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