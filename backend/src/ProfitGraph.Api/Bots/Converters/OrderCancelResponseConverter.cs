using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class OrderCancelResponseConverter
{
    public static OrderCancelResponse ToResponse(this OrderCancelInternal cancel)
    {
        return new OrderCancelResponse(
            cancel.Id,
            cancel.BotId,
            cancel.EngineOrderId,
            cancel.Status,
            cancel.RejectionReason,
            cancel.CreatedAt,
            cancel.ResolvedAt);
    }
}