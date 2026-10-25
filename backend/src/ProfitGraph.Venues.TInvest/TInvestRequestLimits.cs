namespace ProfitGraph.Venues.TInvest;

/// <summary>
///     Requests per minute that one token may send to each service group. They are local ceilings well below what the
///     Invest API documents, so a running job and interactive lookups leave headroom (Profit Fox Designer ADR 0074).
/// </summary>
public sealed class TInvestRequestLimits
{
    public int Users { get; init; } = 50;

    public int Instruments { get; init; } = 150;

    public int MarketData { get; init; } = 300;

    public int Orders { get; init; } = 50;

    public int Operations { get; init; } = 100;

    internal bool IsValid()
    {
        return new[] { Users, Instruments, MarketData, Orders, Operations }.All(limit => limit is > 0 and <= 1000);
    }
}