using System.Globalization;
using System.Text.Json;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Builds Bybit Spot order bodies from engine orders: prices and quantities round down to the instrument's steps, and
///     an order below Bybit's minimums is rejected before anything is sent. A market buy with a quote budget is sized in
///     quote, every other market order in base.
/// </summary>
internal static class BybitOrderRequests
{
    /// <summary>
    ///     The JSON body of the order, or null when it is below the instrument's minimums. <paramref name="lastPrice" />
    ///     values a market sell for the minimum check.
    /// </summary>
    public static string? Create(BybitBotContext context, string clientOrderId, OrderSnapshot order, decimal? lastPrice)
    {
        InstrumentRules rules = context.Instrument.Rules;
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["category"] = "spot",
            ["symbol"] = context.Symbol,
            ["side"] = order.Side == OrderSide.Buy ? "Buy" : "Sell",
            ["orderType"] = order.Type == OrderType.Limit ? "Limit" : "Market",
            ["orderLinkId"] = clientOrderId,
            ["isLeverage"] = 0
        };
        decimal quantity;

        if (order.Type == OrderType.Limit)
        {
            if (order.Quantity is not { } limitQuantity || order.LimitPrice is not { } limitPrice)
            {
                return null;
            }

            quantity = RoundDown(limitQuantity, rules.QuantityStep);
            decimal price = RoundDown(limitPrice, rules.PriceTick);

            if (quantity <= 0m || quantity < rules.MinQuantity || price <= 0m || quantity * price < rules.MinNotional)
            {
                return null;
            }

            body["price"] = Text(price);
        }
        else if (order.Side == OrderSide.Buy && order.QuoteBudget is > 0m)
        {
            quantity = RoundDown(order.QuoteBudget.Value, context.Instrument.QuotePrecisionIncrement);
            body["marketUnit"] = "quoteCoin";

            if (quantity < rules.MinNotional)
            {
                return null;
            }
        }
        else if (order.Quantity is > 0m)
        {
            quantity = RoundDown(order.Quantity.Value, rules.QuantityStep);
            body["marketUnit"] = "baseCoin";

            if (quantity <= 0m || quantity < rules.MinQuantity || lastPrice is not { } price || quantity * price < rules.MinNotional)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        body["qty"] = Text(quantity);

        return JsonSerializer.Serialize(body);
    }

    private static decimal RoundDown(decimal value, decimal step)
    {
        return step > 0m ? decimal.Floor(value / step) * step : value;
    }

    private static string Text(decimal value)
    {
        return value.ToString("0.############################", CultureInfo.InvariantCulture);
    }
}
