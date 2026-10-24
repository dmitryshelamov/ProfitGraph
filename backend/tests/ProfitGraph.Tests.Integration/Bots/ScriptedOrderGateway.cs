using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The order gateway of the test venue: it sends nothing anywhere, and a test pushes the venue's updates by hand.
/// </summary>
public sealed class ScriptedOrderGateway : IOrderGateway
{
    private readonly ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>> _updates = new ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>>();

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId)
    {
        return new ClientOrderId($"pgtest-{botId.Value:N}-{engineOrderId}");
    }

    public async IAsyncEnumerable<ExternalOrderUpdate> Updates(BotId botId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<ExternalOrderUpdate> channel = _updates.GetOrAdd(botId, _ => Channel.CreateUnbounded<ExternalOrderUpdate>());

        await foreach (ExternalOrderUpdate update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }
    }

    public void Push(BotId botId, ExternalOrderUpdate update)
    {
        _updates.GetOrAdd(botId, _ => Channel.CreateUnbounded<ExternalOrderUpdate>()).Writer.TryWrite(update);
    }
}