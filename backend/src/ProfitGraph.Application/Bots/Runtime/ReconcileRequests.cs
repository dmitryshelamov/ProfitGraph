using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     What a venue is asked when a bot opens or when its stopped orders are checked again (ADR 0030): the orders of the
///     bot's engine and intents whose venue state is not known, and the position the engine expects.
/// </summary>
internal static class ReconcileRequests
{
    public static BotReconcileInternal For(Bot bot, GraphEngineCheckpoint? checkpoint, IOrderGateway gateway, IReadOnlyList<OrderIntent> stored)
    {
        return new BotReconcileInternal(bot.Id, bot.AllowForeignOrders, Orders(bot.Id, checkpoint, gateway, stored), checkpoint?.Account.BaseTotal);
    }

    /// <summary>
    ///     Engine orders that are not final, and orders of submits and cancels whose delivery may have happened or failed.
    ///     An order may have reached the venue once the engine saw it accepted or a place request may have left. A submit the
    ///     owner confirmed absent, or an accepted order the owner confirmed removed by the venue, says so, for the gateway to check.
    /// </summary>
    private static IReadOnlyList<ReconcileOrderInternal> Orders(BotId botId, GraphEngineCheckpoint? checkpoint, IOrderGateway gateway, IReadOnlyList<OrderIntent> stored)
    {
        var orders = new SortedDictionary<long, bool>();

        foreach (OrderSnapshot order in checkpoint?.Account.Orders ?? [])
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
                    intent.Status is OrderIntentStatus.Pending or OrderIntentStatus.Failed or OrderIntentStatus.Sent),
                checkpoint?.Account.Progress.FirstOrDefault(progress => progress.OrderId == pair.Key)?.GrossBase ?? 0m,
                stored.FirstOrDefault(intent => intent.Kind == OrderIntentKind.Submit && intent.EngineOrderId == pair.Key)?.PlaceStartedAt))
        ];
    }
}
