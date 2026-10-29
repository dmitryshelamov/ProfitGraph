using System.Globalization;
using System.Text.Json;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A Spot or USDT-perpetual order as Bybit reports it, with cumulative execution and fees by asset. A linear order reports one
///     fee in its settle coin (ADR 0061); a rebate below zero is not a fee and counts as none.
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
    public static BybitSpotOrder? Parse(JsonElement item, string? linearFeeAsset = null)
    {
        var fees = new Dictionary<string, decimal>(StringComparer.Ordinal);
        bool hasFeeDetail = item.TryGetProperty("cumFeeDetail", out JsonElement detail) && detail.ValueKind == JsonValueKind.Object;

        if (linearFeeAsset is not null)
        {
            hasFeeDetail = item.TryGetProperty("cumExecFee", out JsonElement linearFee) && linearFee.ValueKind == JsonValueKind.String;

            if (hasFeeDetail &&
                decimal.Parse(linearFee.GetString()!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) is var paid)
            {
                fees[linearFeeAsset] = Math.Max(paid, val2: 0m);
            }
        }
        else if (hasFeeDetail)
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