namespace ProfitGraph.Engine.Market;

/// <summary>
///     Explicit reached simulation cursor and inherited earliest order eligibility; no operational clock is involved.
/// </summary>
public sealed record ReachedMarketContext
{
    public Candle Candle { get; }
    public int CandleIndex { get; }
    public DateTimeOffset CandleCloseTime { get; }
    public PricePhase Phase { get; }
    public decimal MarketPrice { get; }
    public int EligibleFromCandleIndex { get; }

    public ReachedMarketContext(Candle candle, int candleIndex, DateTimeOffset candleCloseTime, PricePhase phase, decimal marketPrice, int eligibleFromCandleIndex)
    {
        ArgumentNullException.ThrowIfNull(candle);
        ArgumentOutOfRangeException.ThrowIfNegative(candleIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketPrice);

        if (candle.OpenTime.Offset != TimeSpan.Zero || candleCloseTime.Offset != TimeSpan.Zero || candleCloseTime <= candle.OpenTime || !Enum.IsDefined(phase) ||
            eligibleFromCandleIndex < candleIndex || (phase is PricePhase.Close or PricePhase.External && eligibleFromCandleIndex <= candleIndex))
        {
            throw new ArgumentException("Invalid reached UTC cursor or order eligibility.");
        }

        Candle = candle;
        CandleIndex = candleIndex;
        CandleCloseTime = candleCloseTime;
        Phase = phase;
        MarketPrice = marketPrice;
        EligibleFromCandleIndex = eligibleFromCandleIndex;
    }
}