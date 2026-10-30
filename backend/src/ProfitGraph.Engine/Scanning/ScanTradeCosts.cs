namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     What a scanner trade loses beyond its price move (ADR 0065), as fractions of its amount. The level trade enters and takes its
///     target with limit orders, which pay <see cref="LimitFeeRate" />, and leaves at its stop with a market order, which pays
///     <see cref="MarketFeeRate" /> and crosses the <see cref="Spread" />.
/// </summary>
public sealed record ScanTradeCosts
{
    public decimal LimitFeeRate { get; }
    public decimal MarketFeeRate { get; }
    public decimal Spread { get; }

    /// <summary>
    ///     What a stopped trade pays: the entry's limit fee, the stop's market fee and the spread its market order crosses.
    /// </summary>
    public decimal AtStop => LimitFeeRate + MarketFeeRate + Spread;

    /// <summary>
    ///     What a trade that reaches its target pays: a limit fee on the entry and on the exit.
    /// </summary>
    public decimal AtTarget => 2 * LimitFeeRate;

    public ScanTradeCosts(decimal limitFeeRate, decimal marketFeeRate, decimal spread)
    {
        if (limitFeeRate is < 0m or >= 1m || marketFeeRate is < 0m or >= 1m || spread is < 0m or >= 1m)
        {
            throw new ArgumentException("Fee rates and the spread are fractions in [0, 1).");
        }

        LimitFeeRate = limitFeeRate;
        MarketFeeRate = marketFeeRate;
        Spread = spread;
    }

    /// <summary>
    ///     The same fee on every order and no spread, as the preset backtest assumes (ADR 0059, section 8).
    /// </summary>
    public static ScanTradeCosts Flat(decimal feeRate)
    {
        return new ScanTradeCosts(feeRate, feeRate, spread: 0m);
    }

    /// <summary>
    ///     The quote a trade of <paramref name="amount" /> loses at its stop, costs included.
    /// </summary>
    public decimal Loss(decimal amount, decimal entry, decimal stop)
    {
        return amount * (Math.Abs(entry - stop) / entry + AtStop);
    }

    /// <summary>
    ///     The quote a trade of <paramref name="amount" /> makes at its target after its fees; negative when they exceed the move.
    /// </summary>
    public decimal Profit(decimal amount, decimal entry, decimal target)
    {
        return amount * (Math.Abs(target - entry) / entry - AtTarget);
    }

    /// <summary>
    ///     Whether the stop is nearer the entry than what a stopped trade pays, so that fees and spread lose more than the price move.
    /// </summary>
    public bool TooTight(decimal entry, decimal stop)
    {
        return Math.Abs(entry - stop) / entry < AtStop;
    }
}