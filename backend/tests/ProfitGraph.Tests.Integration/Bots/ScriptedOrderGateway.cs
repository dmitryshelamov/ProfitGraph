using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The order gateway of the test venue: it sends nothing anywhere. It remembers what it placed, so a lookup finds an
///     order placed before a failure, and a test pushes the venue's updates or scripts failures and rejections by hand.
/// </summary>
public sealed class ScriptedOrderGateway : IOrderGateway
{
    private readonly ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>> _updates = new ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>>();
    private readonly ConcurrentDictionary<string, OrderSubmitInternal> _placed = new ConcurrentDictionary<string, OrderSubmitInternal>();
    private readonly ConcurrentQueue<ClientOrderId> _cancels = new ConcurrentQueue<ClientOrderId>();
    private int _places;
    private int _lookUps;
    private int _failuresAfterPlace;

    public int Places => Volatile.Read(ref _places);
    public int LookUps => Volatile.Read(ref _lookUps);
    public IReadOnlyCollection<ClientOrderId> Cancels => _cancels;

    /// <summary>
    ///     A definitive rejection for every following submit, or null to accept them.
    /// </summary>
    public RejectionReason? RejectWith { get; set; }

    /// <summary>
    ///     Fails every submit after its place started, as a lost response would.
    /// </summary>
    public bool FailEveryPlace { get; set; }

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId)
    {
        return new ClientOrderId($"pgtest-{botId.Value:N}-{engineOrderId}");
    }

    public async IAsyncEnumerable<ExternalOrderUpdate> Updates(BotId botId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (ExternalOrderUpdate update in Channel(botId).Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }
    }

    public async Task<ExternalOrderUpdate?> Submit(OrderSubmitInternal submit, Func<CancellationToken, Task> markPlaceStarted, CancellationToken cancellationToken)
    {
        if (submit.MustLookUpBeforeSubmit)
        {
            Interlocked.Increment(ref _lookUps);

            if (_placed.ContainsKey(submit.ClientOrderId.Value))
            {
                Push(submit.BotId, ExternalOrderUpdate.Accept(submit.Order.Id));

                return null;
            }

            throw new IOException("The scripted venue cannot prove the order absent.");
        }

        await markPlaceStarted(cancellationToken);
        Interlocked.Increment(ref _places);

        if (RejectWith is { } reason)
        {
            return ExternalOrderUpdate.Reject(submit.Order.Id, reason, "The scripted venue rejected the order.");
        }

        _placed[submit.ClientOrderId.Value] = submit;

        if (FailEveryPlace || Interlocked.Decrement(ref _failuresAfterPlace) >= 0)
        {
            throw new IOException("The scripted venue lost the response.");
        }

        Push(submit.BotId, ExternalOrderUpdate.Accept(submit.Order.Id));

        return null;
    }

    public Task Cancel(BotId botId, ClientOrderId clientOrderId, CancellationToken cancellationToken)
    {
        _cancels.Enqueue(clientOrderId);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Fails the next <paramref name="count" /> submits after their place started; a retry then looks them up.
    /// </summary>
    public void FailAfterPlace(int count)
    {
        Interlocked.Exchange(ref _failuresAfterPlace, count);
    }

    private void Push(BotId botId, ExternalOrderUpdate update)
    {
        Channel(botId).Writer.TryWrite(update);
    }

    private Channel<ExternalOrderUpdate> Channel(BotId botId)
    {
        return _updates.GetOrAdd(botId, _ => System.Threading.Channels.Channel.CreateUnbounded<ExternalOrderUpdate>());
    }
}