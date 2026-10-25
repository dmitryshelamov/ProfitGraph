namespace ProfitGraph.Venues.Bybit.Simulation;

/// <summary>
///     Section <c>Live:Simulation</c>: the in-process stand-in for Bybit order execution that end-to-end tests trade
///     against. The Worker accepts it only in the <c>Testing</c> environment with trading disabled.
/// </summary>
public sealed class BybitSimulationOptions
{
    public const string SectionName = "Live:Simulation";

    public bool Enabled { get; init; }

    /// <summary>
    ///     The price every market order fills at.
    /// </summary>
    public decimal MarketPrice { get; init; }

    /// <summary>
    ///     The fee of a fill as a fraction: in the base asset for a buy, in the quote asset for a sell.
    /// </summary>
    public decimal FeeRate { get; init; } = 0.001m;

    public bool IsValid()
    {
        return MarketPrice > 0m && FeeRate is >= 0m and < 0.1m;
    }
}