using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Numbers;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotOpenOrderResponseConverter
{
    public static BotOpenOrderResponse ToResponse(this BotOpenOrderInternal order)
    {
        return new BotOpenOrderResponse(
            order.EngineOrderId,
            order.Tag,
            order.Side,
            order.Type,
            order.Status,
            order.Quantity is { } quantity ? ExactDecimalText.Format(quantity) : null,
            order.LimitPrice is { } price ? ExactDecimalText.Format(price) : null,
            order.QuoteBudget is { } budget ? ExactDecimalText.Format(budget) : null,
            ExactDecimalText.Format(order.FilledBase));
    }
}
