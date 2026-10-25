using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class ManualOrderResponseConverter
{
    public static ManualOrderResponse ToResponse(this ManualOrderInternal order)
    {
        return new ManualOrderResponse(
            order.Id,
            order.BotId,
            order.Side,
            ExactGraphNumberParser.Format(order.Amount),
            order.Status,
            order.EngineOrderId,
            order.RejectionReason,
            order.CreatedAt,
            order.ResolvedAt);
    }
}