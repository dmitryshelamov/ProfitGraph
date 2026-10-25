using Google.Protobuf.WellKnownTypes;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     Exact conversions of Invest API values: a quotation is whole units plus billionths, never a float.
/// </summary>
internal static class TInvestValues
{
    private const decimal Billion = 1_000_000_000m;

    public static decimal ToDecimal(this Quotation quotation)
    {
        return quotation.Units + quotation.Nano / Billion;
    }

    public static decimal ToDecimal(this MoneyValue money)
    {
        return money.Units + money.Nano / Billion;
    }

    public static Quotation ToQuotation(this decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));

        return new Quotation { Units = units, Nano = decimal.ToInt32((value - units) * Billion) };
    }

    public static DateTimeOffset ToUtc(this Timestamp timestamp)
    {
        return new DateTimeOffset(timestamp.ToDateTime(), TimeSpan.Zero);
    }

    public static Timestamp ToTimestamp(this DateTimeOffset time)
    {
        return Timestamp.FromDateTimeOffset(time);
    }
}