using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     When the owner may confirm an order absent at the venue. Two cases share one confirmation (Profit Fox Designer ADR 0055
///     with its 2026-10-18 follow-ups, ADR 0028): a submit absent that was never confirmed by the venue, whose delivery stopped
///     the bot, and an order the venue had accepted that it no longer knows, such as an expired day order, with nothing
///     executed. The venue's gateway checks either one before the engine order ends.
/// </summary>
internal static class OrderAbsence
{
    public static bool IsUnresolved(Bot bot, OrderIntent intent, BotCheckpoint? checkpoint, IVenueDescriptor? venue)
    {
        return (intent is { Kind: OrderIntentKind.Submit, PlaceStartedAt: not null } &&
                intent.Status is OrderIntentStatus.Pending or OrderIntentStatus.Sending or OrderIntentStatus.Failed &&
                Order(intent, checkpoint) is { Status: OrderStatus.PendingSubmit or OrderStatus.Active or OrderStatus.PendingCancel }) ||
            IsAcceptedAndStopped(bot, intent, checkpoint, venue);
    }

    public static bool CanConfirm(Bot bot, OrderIntent intent, BotCheckpoint? checkpoint, IVenueDescriptor? venue)
    {
        ExternalOrderProgressCheckpoint? progress = checkpoint?.Engine.Account.Progress.FirstOrDefault(item => item.OrderId == intent.EngineOrderId);

        if (IsAcceptedAndStopped(bot, intent, checkpoint, venue))
        {
            return intent.ConfirmedAbsentAt is null && progress is not { GrossBase: > 0m };
        }

        return venue?.ConfirmsOrderAbsence == true && bot.IsDegraded &&
            bot.StatusReason is BotStatusReason.UnresolvedOrder or BotStatusReason.IntentFailed &&
            intent is { Kind: OrderIntentKind.Submit, PlaceStartedAt: not null, ConfirmedAbsentAt: null } &&
            intent.Status is OrderIntentStatus.Pending or OrderIntentStatus.Failed &&
            Order(intent, checkpoint) is { Status: OrderStatus.PendingSubmit } &&
            progress is { Confirmed: false, GrossBase: 0m };
    }

    /// <summary>
    ///     Whether the order was delivered and accepted by the venue, is still working in the engine, and the bot stopped
    ///     because the venue's answer about it is unresolved.
    /// </summary>
    public static bool IsAcceptedAndStopped(Bot bot, OrderIntent intent, BotCheckpoint? checkpoint, IVenueDescriptor? venue)
    {
        return venue?.ConfirmsExpiredOrders == true && bot is { IsDegraded: true, StatusReason: BotStatusReason.UnresolvedOrder } &&
            intent is { Kind: OrderIntentKind.Submit, PlaceStartedAt: not null, Status: OrderIntentStatus.Sent } &&
            Order(intent, checkpoint) is { Status: OrderStatus.Active or OrderStatus.PendingCancel };
    }

    public static OrderSnapshot? Order(OrderIntent intent, BotCheckpoint? checkpoint)
    {
        return checkpoint?.Engine.Account.Orders.FirstOrDefault(order => order.Id == intent.EngineOrderId);
    }
}