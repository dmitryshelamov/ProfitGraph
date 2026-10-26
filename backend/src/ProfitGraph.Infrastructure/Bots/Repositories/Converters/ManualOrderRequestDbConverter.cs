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
            Enum.Parse<OrderType>(row.OrderType),
            row.Amount,
            row.LimitPrice,
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
            OrderType = request.Type.ToString(),
            Amount = request.Amount,
            LimitPrice = request.LimitPrice,
            Status = request.Status.ToString(),
            EngineOrderId = request.EngineOrderId,
            RejectionReason = request.RejectionReason,
            CreatedAt = request.CreatedAt,
            ResolvedAt = request.ResolvedAt
        };
    }
}