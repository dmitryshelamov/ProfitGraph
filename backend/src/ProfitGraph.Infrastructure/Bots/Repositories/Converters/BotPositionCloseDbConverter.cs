using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.PositionCloses;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotPositionCloseDbConverter
{
    public static BotPositionClose ToDomain(this BotPositionCloseDb row)
    {
        return BotPositionClose.Restore(
            new BotPositionCloseId(row.Id),
            new BotId(row.BotId),
            row.StopAfter,
            Enum.Parse<BotPositionCloseStatus>(row.Status),
            row.CancelledOrders,
            row.EngineOrderId,
            row.FailureReason,
            row.CreatedAt,
            row.ResolvedAt);
    }

    public static BotPositionCloseDb ToDb(this BotPositionClose close)
    {
        return new BotPositionCloseDb
        {
            Id = close.Id.Value,
            BotId = close.BotId.Value,
            StopAfter = close.StopAfter,
            Status = close.Status.ToString(),
            CancelledOrders = close.CancelledOrders,
            EngineOrderId = close.EngineOrderId,
            FailureReason = close.FailureReason,
            CreatedAt = close.CreatedAt,
            ResolvedAt = close.ResolvedAt
        };
    }
}