using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Application.Bots.Converters;

internal static class BotPositionCloseInternalConverter
{
    public static BotPositionCloseInternal ToInternal(this BotPositionClose close)
    {
        return new BotPositionCloseInternal(
            close.Id.Value,
            close.BotId.Value,
            close.StopAfter,
            close.Status,
            close.CancelledOrders,
            close.EngineOrderId,
            close.FailureReason,
            close.CreatedAt,
            close.ResolvedAt);
    }
}