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
    private int _reconcileFailures;
    private int _reconciles;

    public int Places => Volatile.Read(ref _places);
    public int LookUps => Volatile.Read(ref _lookUps);
    public IReadOnlyCollection<ClientOrderId> Cancels => _cancels;
    public int Reconciles => Volatile.Read(ref _reconciles);

    /// <summary>
    ///     The number of places when the last reconciliation started.
    /// </summary>
    public int PlacesBeforeLastReconcile { get; private set; }

    /// <summary>
    ///     A safety reason every following reconciliation reports, or null to let the bot trade.
    /// </summary>
    public BotStatusReason? ReconcileDegradation { get; set; }

    /// <summary>
    ///     A definitive rejection for every following submit, or null to accept them.
    /// </summary>
    public RejectionReason? RejectWith { get; set; }

    /// <summary>
    ///     Fails every submit after its place started, as a lost response would.
    /// </summary>
    public bool FailEveryPlace { get; set; }

    /// <summary>
    ///     Loses every place request after it may have left, so the venue never has the order.
    /// </summary>
    public bool LoseEveryPlace { get; set; }

    /// <summary>
    ///     The client order identifiers reconciliation passed as confirmed absent by the owner.
    /// </summary>
    public ConcurrentQueue<ClientOrderId> ConfirmedAbsent { get; } = new ConcurrentQueue<ClientOrderId>();

    /// <summary>
    ///     When set, a reconciliation closes every order it finds as cancelled by the venue, as T-Invest does for a DAY order its
    ///     exchange removed (ADR 0030): the engine is told <c>CancelConfirmed</c> for it.
    /// </summary>
    public bool CancelEveryFoundOrder { get; set; }

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

        if (LoseEveryPlace)
        {
            throw new IOException("The scripted venue lost the request.");
        }

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
    ///     Finds every order it placed, as accepted and still active; the rest are unknown to it.
    /// </summary>
    public Task<BotReconciliationInternal> Reconcile(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        PlacesBeforeLastReconcile = Places;
        Interlocked.Increment(ref _reconciles);

        if (Interlocked.Decrement(ref _reconcileFailures) >= 0)
        {
            throw new IOException("The scripted venue cannot answer now.");
        }

        ReconcileOrderInternal[] found = [.. request.Orders.Where(order => _placed.ContainsKey(order.ClientOrderId.Value))];
        ReconcileOrderInternal[] absent = [.. request.Orders.Where(order => order.OwnerConfirmedAbsent && !_placed.ContainsKey(order.ClientOrderId.Value))];

        foreach (ReconcileOrderInternal order in absent)
        {
            ConfirmedAbsent.Enqueue(order.ClientOrderId);
        }

        return Task.FromResult(
            new BotReconciliationInternal(
                [
                    .. found.Select(
                        order => CancelEveryFoundOrder ? ExternalOrderUpdate.ConfirmCancel(order.EngineOrderId) : ExternalOrderUpdate.Accept(order.EngineOrderId)),
                    .. absent.Select(order => ExternalOrderUpdate.Reject(
                        order.EngineOrderId,
                        RejectionReason.OwnerConfirmedAbsent,
                        "The scripted venue has no such order.",
                        ownerConfirmedAbsent: true))
                ],
                [
                    .. found.Select(order => new ReconciledOrderInternal(order.ClientOrderId, IsFinal: CancelEveryFoundOrder)),
                    .. absent.Select(order => new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true))
                ],
                ReconcileDegradation));
    }

    /// <summary>
    ///     Fails the next <paramref name="count" /> reconciliations as a venue that cannot answer.
    /// </summary>
    public void FailReconcile(int count)
    {
        Interlocked.Exchange(ref _reconcileFailures, count);
    }

    /// <summary>
    ///     Fails the next <paramref name="count" /> submits after their place started; a retry then looks them up.
    /// </summary>
    public void FailAfterPlace(int count)
    {
        Interlocked.Exchange(ref _failuresAfterPlace, count);
    }

    /// <summary>
    ///     Delivers a venue update to the bot's runner, as the venue's stream would.
    /// </summary>
    public void Push(BotId botId, ExternalOrderUpdate update)
    {
        Channel(botId).Writer.TryWrite(update);
    }

    private Channel<ExternalOrderUpdate> Channel(BotId botId)
    {
        return _updates.GetOrAdd(botId, _ => System.Threading.Channels.Channel.CreateUnbounded<ExternalOrderUpdate>());
    }
}