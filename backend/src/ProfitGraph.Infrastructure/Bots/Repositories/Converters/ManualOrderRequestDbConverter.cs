using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class ManualOrderRequestDbConverter
{
    public static ManualOrderRequest ToDomain(this ManualOrderRequestDb row)
    {
        return ManualOrderRequest.Restore(
            new ManualOrderRequestId(row.Id),
            new BotId(row.BotId),
            Enum.Parse<OrderSide>(row.Side),
            row.Amount,
            Enum.Parse<ManualOrderStatus>(row.Status),
            row.EngineOrderId,
            row.RejectionReason,
            row.CreatedAt,
            row.ResolvedAt);
    }

    public static ManualOrderRequestDb ToDb(this ManualOrderRequest request)
    {
        return new ManualOrderRequestDb
        {
            Id = request.Id.Value,
            BotId = request.BotId.Value,
            Side = request.Side.ToString(),
            Amount = request.Amount,
            Status = request.Status.ToString(),
            EngineOrderId = request.EngineOrderId,
            RejectionReason = request.RejectionReason,
            CreatedAt = request.CreatedAt,
            ResolvedAt = request.ResolvedAt
        };
    }
}