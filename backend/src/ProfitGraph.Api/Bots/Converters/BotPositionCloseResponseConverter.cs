using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotPositionCloseResponseConverter
{
    public static BotPositionCloseResponse ToResponse(this BotPositionCloseInternal close)
    {
        return new BotPositionCloseResponse(
            close.Id,
            close.BotId,
            close.StopAfter,
            close.Status,
            close.CancelledOrders,
            close.EngineOrderId,
            close.FailureReason,
            close.CreatedAt,
            close.ResolvedAt);
    }
}