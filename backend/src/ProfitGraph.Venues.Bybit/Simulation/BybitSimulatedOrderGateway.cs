using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Venues.Bybit.Simulation;

/// <summary>
///     Bybit order execution simulated in memory for end-to-end tests; nothing leaves the process. A market order fills at
///     once at the configured price with the configured fee, a limit order rests until it is cancelled, and a lookup or a
///     reconciliation finds what this process placed. A restart forgets every order, so a bot with orders in flight then
///     stays closed as it would against a venue that cannot find them.
/// </summary>
internal sealed class BybitSimulatedOrderGateway(BybitSimulationOptions options) : IOrderGateway
{
    private const int BasePrecision = 8;

    private readonly ConcurrentDictionary<string, BybitSimulatedOrder> _orders = new ConcurrentDictionary<string, BybitSimulatedOrder>();
    private readonly ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>> _updates = new ConcurrentDictionary<BotId, Channel<ExternalOrderUpdate>>();

    public VenueId Venue => BybitVenue.VenueId;

    public ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId)
    {
        return BybitClientOrderIds.For(botId, engineOrderId);
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
        if (submit.MustLookUpBeforeSubmit && _orders.TryGetValue(submit.ClientOrderId.Value, out BybitSimulatedOrder? existing))
        {
            Publish(existing.BotId, existing.Last);

            return null;
        }

        // The simulated venue knows every order of this process, so an empty lookup proves the order absent.
        await markPlaceStarted(cancellationToken);
        OrderSnapshot order = submit.Order;

        if (order.Type == OrderType.Limit)
        {
            Record(submit, ExternalOrderUpdate.Accept(order.Id));

            return null;
        }

        decimal price = options.MarketPrice;
        decimal grossBase = order.Quantity ?? decimal.Round(order.QuoteBudget!.Value / price, BasePrecision, MidpointRounding.ToZero);

        if (grossBase <= 0m)
        {
            return ExternalOrderUpdate.Reject(order.Id, RejectionReason.ZeroQuantity, "The simulated order is below one base unit.");
        }

        decimal grossQuote = grossBase * price;
        decimal fee = order.Side == OrderSide.Buy ? grossBase * options.FeeRate : grossQuote * options.FeeRate;
        Publish(submit.BotId, ExternalOrderUpdate.Accept(order.Id));
        Record(submit, ExternalOrderUpdate.Final(order.Id, grossBase, grossQuote, fee));

        return null;
    }

    public Task Cancel(BotId botId, ClientOrderId clientOrderId, CancellationToken cancellationToken)
    {
        if (_orders.TryGetValue(clientOrderId.Value, out BybitSimulatedOrder? order) && !order.IsFinal)
        {
            ExternalOrderUpdate cancelled = ExternalOrderUpdate.ConfirmCancel(order.Last.OrderId);
            _orders[clientOrderId.Value] = order with { Last = cancelled };
            Publish(botId, cancelled);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Reports the last state of every order of the request this process placed; nothing else trades here.
    /// </summary>
    public Task<BotReconciliationInternal> Reconcile(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        var updates = new List<ExternalOrderUpdate>();
        var found = new List<ReconciledOrderInternal>();

        foreach (ReconcileOrderInternal order in request.Orders)
        {
            if (_orders.TryGetValue(order.ClientOrderId.Value, out BybitSimulatedOrder? known))
            {
                updates.Add(known.Last);
                found.Add(new ReconciledOrderInternal(order.ClientOrderId, known.IsFinal));
            }
        }

        return Task.FromResult(new BotReconciliationInternal(updates, found, Degradation: null));
    }

    private void Record(OrderSubmitInternal submit, ExternalOrderUpdate update)
    {
        _orders[submit.ClientOrderId.Value] = new BybitSimulatedOrder(submit.BotId, update);
        Publish(submit.BotId, update);
    }

    private void Publish(BotId botId, ExternalOrderUpdate update)
    {
        Channel(botId).Writer.TryWrite(update);
    }

    private Channel<ExternalOrderUpdate> Channel(BotId botId)
    {
        return _updates.GetOrAdd(botId, _ => System.Threading.Channels.Channel.CreateUnbounded<ExternalOrderUpdate>());
    }
}