using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Numbers;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotUnresolvedOrderResponseConverter
{
    public static BotUnresolvedOrderResponse ToResponse(this BotUnresolvedOrderInternal order)
    {
        return new BotUnresolvedOrderResponse(
            order.IntentId,
            order.EngineOrderId,
            order.Side,
            order.Type,
            order.Quantity is { } quantity ? ExactDecimalText.Format(quantity) : null,
            order.QuoteBudget is { } budget ? ExactDecimalText.Format(budget) : null,
            order.LimitPrice is { } price ? ExactDecimalText.Format(price) : null,
            order.CreatedAt,
            order.PlaceStartedAt,
            order.CanConfirmAbsent,
            order.ConfirmedAbsentAt);
    }
}