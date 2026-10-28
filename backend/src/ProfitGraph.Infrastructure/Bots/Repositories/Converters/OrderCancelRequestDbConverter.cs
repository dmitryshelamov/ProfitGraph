using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderCancels;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class OrderCancelRequestDbConverter
{
    public static OrderCancelRequest ToDomain(this OrderCancelRequestDb row)
    {
        return OrderCancelRequest.Restore(
            new OrderCancelRequestId(row.Id),
            new BotId(row.BotId),
            row.EngineOrderId,
            Enum.Parse<OrderCancelStatus>(row.Status),
            row.RejectionReason,
            row.CreatedAt,
            row.ResolvedAt);
    }

    public static OrderCancelRequestDb ToDb(this OrderCancelRequest request)
    {
        return new OrderCancelRequestDb
        {
            Id = request.Id.Value,
            BotId = request.BotId.Value,
            EngineOrderId = request.EngineOrderId,
            Status = request.Status.ToString(),
            RejectionReason = request.RejectionReason,
            CreatedAt = request.CreatedAt,
            ResolvedAt = request.ResolvedAt
        };
    }
}