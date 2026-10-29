namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Immutable boundaries and cumulative counters of one observed continuous-account segment.
/// </summary>
internal readonly record struct MetricsSegmentState(
    int StartIndex,
    int EndExclusive,
    int ObservedCandles,
    decimal? BoundaryEquity,
    decimal EndEquity,
    decimal BoundaryRealized,
    decimal BoundaryUnrealized,
    decimal EndRealized,
    decimal EndUnrealized,
    decimal BoundaryFeesBase,
    decimal BoundaryFeesQuote,
    decimal EndFeesBase,
    decimal EndFeesQuote,
    decimal Peak,
    decimal MaxDrawdownPercent,
    long Executions,
    decimal MaxDeployed,
    decimal MaxCommitted,
    int InPositionCandles,
    int UnderWaterCandles,
    int UnderWaterRun,
    int LongestUnderWaterRun)
{
    internal static MetricsSegmentState Empty(int startIndex, int endExclusive)
    {
        return new MetricsSegmentState(
            startIndex,
            endExclusive,
            ObservedCandles: 0,
            BoundaryEquity: null,
            EndEquity: 0m,
            BoundaryRealized: 0m,
            BoundaryUnrealized: 0m,
            EndRealized: 0m,
            EndUnrealized: 0m,
            BoundaryFeesBase: 0m,
            BoundaryFeesQuote: 0m,
            EndFeesBase: 0m,
            EndFeesQuote: 0m,
            Peak: 0m,
            MaxDrawdownPercent: 0m,
            Executions: 0,
            MaxDeployed: 0m,
            MaxCommitted: 0m,
            InPositionCandles: 0,
            UnderWaterCandles: 0,
            UnderWaterRun: 0,
            LongestUnderWaterRun: 0);
    }

    internal MetricsSegmentState Start(decimal equity, decimal realized, decimal unrealized, decimal feesBase, decimal feesQuote)
    {
        return this with
        {
            BoundaryEquity = equity, EndEquity = equity, BoundaryRealized = realized, BoundaryUnrealized = unrealized,
            EndRealized = realized, EndUnrealized = unrealized, BoundaryFeesBase = feesBase, BoundaryFeesQuote = feesQuote,
            EndFeesBase = feesBase, EndFeesQuote = feesQuote, Peak = equity
        };
    }

    internal MetricsSegmentState Advance(CandleMetricObservation candle, int executions)
    {
        decimal peak = Math.Max(Peak, candle.Equity);
        decimal drawdown = peak == 0m ? 0m : (peak - candle.Equity) / peak * 100m;
        bool underWater = candle.InPosition && candle.UnrealizedPnl < 0m;
        int underWaterRun = underWater ? checked(UnderWaterRun + 1) : 0;

        return this with
        {
            ObservedCandles = checked(ObservedCandles + 1), EndEquity = candle.Equity,
            EndRealized = candle.RealizedPnl, EndUnrealized = candle.UnrealizedPnl,
            EndFeesBase = candle.FeesBase, EndFeesQuote = candle.FeesQuote,
            Peak = peak, MaxDrawdownPercent = Math.Max(MaxDrawdownPercent, drawdown), Executions = checked(Executions + executions),
            MaxDeployed = Math.Max(MaxDeployed, candle.CostBasis), MaxCommitted = Math.Max(MaxCommitted, checked(candle.CostBasis + candle.QuoteReserved)),
            InPositionCandles = candle.InPosition ? checked(InPositionCandles + 1) : InPositionCandles,
            UnderWaterCandles = underWater ? checked(UnderWaterCandles + 1) : UnderWaterCandles, UnderWaterRun = underWaterRun,
            LongestUnderWaterRun = Math.Max(LongestUnderWaterRun, underWaterRun)
        };
    }
}