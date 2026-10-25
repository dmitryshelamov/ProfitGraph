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

    public static DateTimeOffset ToUtc(this Timestamp timestamp)
    {
        return new DateTimeOffset(timestamp.ToDateTime(), TimeSpan.Zero);
    }

    public static Timestamp ToTimestamp(this DateTimeOffset time)
    {
        return Timestamp.FromDateTimeOffset(time);
    }
}