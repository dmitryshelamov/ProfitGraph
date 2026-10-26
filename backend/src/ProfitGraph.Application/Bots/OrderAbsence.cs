using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     When the owner may confirm a submit absent at the venue (Profit Fox Designer ADR 0055 with its 2026-10-18 follow-ups):
///     the bot was stopped for safety by that unknown delivery, the venue's gateway checks a confirmation, and the engine's
///     order is a submit still pending with nothing filled.
/// </summary>
internal static class OrderAbsence
{
    public static bool IsUnresolved(OrderIntent intent, BotCheckpoint? checkpoint)
    {
        return intent is { Kind: OrderIntentKind.Submit, PlaceStartedAt: not null } &&
            intent.Status is OrderIntentStatus.Pending or OrderIntentStatus.Sending or OrderIntentStatus.Failed &&
            Order(intent, checkpoint) is { Status: OrderStatus.PendingSubmit or OrderStatus.Active or OrderStatus.PendingCancel };
    }

    public static bool CanConfirm(Bot bot, OrderIntent intent, BotCheckpoint? checkpoint, IVenueDescriptor? venue)
    {
        ExternalOrderProgressCheckpoint? progress = checkpoint?.Engine.Account.Progress.FirstOrDefault(item => item.OrderId == intent.EngineOrderId);

        return venue?.ConfirmsOrderAbsence == true && bot.IsDegraded &&
            bot.StatusReason is BotStatusReason.UnresolvedOrder or BotStatusReason.IntentFailed &&
            intent is { Kind: OrderIntentKind.Submit, PlaceStartedAt: not null, ConfirmedAbsentAt: null } &&
            intent.Status is OrderIntentStatus.Pending or OrderIntentStatus.Failed &&
            Order(intent, checkpoint) is { Status: OrderStatus.PendingSubmit } &&
            progress is { Confirmed: false, GrossBase: 0m };
    }

    public static OrderSnapshot? Order(OrderIntent intent, BotCheckpoint? checkpoint)
    {
        return checkpoint?.Engine.Account.Orders.FirstOrDefault(order => order.Id == intent.EngineOrderId);
    }
}