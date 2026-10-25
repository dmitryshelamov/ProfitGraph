using System.Globalization;
using System.Text.Json;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A Spot order as Bybit reports it, with cumulative execution and fees by asset.
/// </summary>
internal sealed record BybitSpotOrder(
    string ClientOrderId,
    string Symbol,
    string Side,
    string Status,
    decimal CumulativeBase,
    decimal CumulativeQuote,
    IReadOnlyDictionary<string, decimal> CumulativeFees)
{
    public bool IsFinal => Status is "Filled" or "Cancelled" or "PartiallyFilledCanceled" or "Rejected" or "Deactivated";

    /// <summary>
    ///     Reads one item of an order list; an executed order without fee detail is incomplete and gives null.
    /// </summary>
    public static BybitSpotOrder? Parse(JsonElement item)
    {
        var fees = new Dictionary<string, decimal>(StringComparer.Ordinal);
        bool hasFeeDetail = item.TryGetProperty("cumFeeDetail", out JsonElement detail) && detail.ValueKind == JsonValueKind.Object;

        if (hasFeeDetail)
        {
            foreach (JsonProperty fee in detail.EnumerateObject())
            {
                fees[fee.Name] = decimal.Parse(fee.Value.GetString()!, CultureInfo.InvariantCulture);
            }
        }

        decimal cumulativeBase = Decimal(item, "cumExecQty");

        return cumulativeBase > 0m && !hasFeeDetail
            ? null
            : new BybitSpotOrder(
                item.GetProperty("orderLinkId").GetString()!,
                item.GetProperty("symbol").GetString()!,
                item.GetProperty("side").GetString()!,
                item.GetProperty("orderStatus").GetString()!,
                cumulativeBase,
                Decimal(item, "cumExecValue"),
                fees);
    }

    private static decimal Decimal(JsonElement item, string name)
    {
        string? text = item.GetProperty(name).GetString();

        return string.IsNullOrEmpty(text) ? 0m : decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}
