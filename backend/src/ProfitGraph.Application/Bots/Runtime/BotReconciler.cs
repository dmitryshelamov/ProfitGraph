using MediatR;

using ProfitGraph.Application.Bots.ApplyBotReconciliation;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Reconciles an opened bot with its venue before its submit gate opens (ADR 0016, Profit Fox Designer ADR 0055): it
///     looks up every engine order that is not final and every submit or cancel whose delivery is unknown, applies what the
///     venue reports to the engine and records the outcome. A failed cancel of an order that is still active, or a safety
///     reason from the venue, degrades the bot; a venue that cannot answer leaves the bot to retry.
/// </summary>
public sealed class BotReconciler(IOrderIntentRepository intents, ISender sender)
{
    public async Task<BotReconcileResult> Reconcile(
        Bot bot,
        LeaseHolderId holder,
        long epoch,
        BotEngineSession session,
        IOrderGateway gateway,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderIntent> stored = await intents.ListByBot(bot.Id, cancellationToken);
        BotReconciliationInternal reconciliation;

        try
        {
            reconciliation = await gateway.Reconcile(
                new BotReconcileInternal(bot.Id, bot.AllowForeignOrders, Orders(bot.Id, session, gateway, stored)),
                cancellationToken);
        }
        catch (OrderGatewaySafetyException exception)
        {
            reconciliation = new BotReconciliationInternal([], [], exception.Reason);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BotReconcileResult.Unavailable;
        }

        foreach (ExternalOrderUpdate update in reconciliation.Updates)
        {
            await session.ProcessExternalUpdate(update, cancellationToken);
        }

        HashSet<string> active = [.. reconciliation.Found.Where(order => !order.IsFinal).Select(order => order.ClientOrderId.Value)];
        BotStatusReason? degradation = reconciliation.Degradation ??
            (stored.Any(intent => intent is { Kind: OrderIntentKind.Cancel, Status: OrderIntentStatus.Failed } && active.Contains(intent.ClientOrderId.Value))
                ? BotStatusReason.UnresolvedOrder
                : null);
        var applied = new ApplyBotReconciliationCommand(
            bot.Id.Value,
            holder.Value,
            epoch,
            [.. reconciliation.Found.Select(order => order.ClientOrderId.Value)],
            [.. reconciliation.Found.Where(order => order.IsFinal).Select(order => order.ClientOrderId.Value)],
            degradation);

        if (!await sender.Send(applied, cancellationToken))
        {
            throw new BotLeaseLostException();
        }

        return new BotReconcileResult(VenueAnswered: true, degradation);
    }

    /// <summary>
    ///     Engine orders that are not final, and orders of submits and cancels whose delivery may have happened or failed.
    ///     An order may have reached the venue once the engine saw it accepted or a place request may have left. A submit the
    ///     owner confirmed absent, or an accepted order the owner confirmed removed by the venue, says so, for the gateway to check.
    /// </summary>
    private static IReadOnlyList<ReconcileOrderInternal> Orders(BotId botId, BotEngineSession session, IOrderGateway gateway, IReadOnlyList<OrderIntent> stored)
    {
        var orders = new SortedDictionary<long, bool>();

        foreach (OrderSnapshot order in session.Checkpoint?.Account.Orders ?? [])
        {
            if (order.Status is OrderStatus.Active or OrderStatus.PendingSubmit or OrderStatus.PendingCancel)
            {
                orders[order.Id] = order.Status != OrderStatus.PendingSubmit;
            }
        }

        foreach (OrderIntent intent in stored.Where(intent => intent is { Kind: OrderIntentKind.Submit, Status: OrderIntentStatus.Deferred }))
        {
            // A deferred submit never left the process, however the engine sees it (ADR 0024).
            orders[intent.EngineOrderId] = false;
        }

        foreach (OrderIntent intent in stored)
        {
            if (intent.Status is OrderIntentStatus.Failed or OrderIntentStatus.Sending || intent is { Status: OrderIntentStatus.Pending, PlaceStartedAt: not null })
            {
                orders.TryAdd(intent.EngineOrderId, value: false);
            }
        }

        return
        [
            .. orders.Select(pair => new ReconcileOrderInternal(
                pair.Key,
                gateway.ClientOrderIdFor(botId, pair.Key),
                pair.Value || stored.Any(intent => intent.Kind == OrderIntentKind.Submit && intent.EngineOrderId == pair.Key &&
                    (intent.PlaceStartedAt is not null || intent.Status == OrderIntentStatus.Sent)),
                stored.Any(intent => intent is { Kind: OrderIntentKind.Submit, ConfirmedAbsentAt: not null } && intent.EngineOrderId == pair.Key &&
                    intent.Status is OrderIntentStatus.Pending or OrderIntentStatus.Failed or OrderIntentStatus.Sent)))
        ];
    }
}