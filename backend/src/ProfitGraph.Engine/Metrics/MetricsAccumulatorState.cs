using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Immutable value state shared by retained and metrics-only completed-candle reporting.
/// </summary>
internal readonly struct MetricsAccumulatorState
{
    private readonly decimal _startingCapital;
    private readonly SampleRanges _ranges;
    private readonly MetricsSegmentState _full;
    private readonly MetricsSegmentState _inSample;
    private readonly MetricsSegmentState? _outOfSample;

    private readonly int? _candleIndex;

    public BacktestMetricsSnapshot Snapshot => Capture(_candleIndex);
    public int ObservedCandles => _full.ObservedCandles;

    public MetricsAccumulatorState(decimal startingCapital, SampleRanges ranges)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startingCapital);
        ArgumentNullException.ThrowIfNull(ranges);

        if (ranges.InSampleBars <= 0 || ranges.OutOfSampleBars < 0 || (long)ranges.InSampleBars + ranges.OutOfSampleBars > int.MaxValue)
        {
            throw new ArgumentException("Supply nonempty valid replay sample ranges.", nameof(ranges));
        }

        _startingCapital = startingCapital;
        _ranges = ranges;
        _full = MetricsSegmentState.Empty(startIndex: 0, ranges.TotalBars).Start(startingCapital, realized: 0m, unrealized: 0m, feesBase: 0m, feesQuote: 0m);
        _inSample = MetricsSegmentState.Empty(startIndex: 0, ranges.InSampleBars).Start(startingCapital, realized: 0m, unrealized: 0m, feesBase: 0m, feesQuote: 0m);
        _outOfSample = ranges.OutOfSampleBars == 0 ? null : MetricsSegmentState.Empty(ranges.InSampleBars, ranges.TotalBars);
        _candleIndex = null;
    }

    private MetricsAccumulatorState(
        decimal startingCapital,
        SampleRanges ranges,
        MetricsSegmentState full,
        MetricsSegmentState inSample,
        MetricsSegmentState? outOfSample,
        int candleIndex)
    {
        _startingCapital = startingCapital;
        _ranges = ranges;
        _full = full;
        _inSample = inSample;
        _outOfSample = outOfSample;
        _candleIndex = candleIndex;
    }

    public MetricsAccumulatorState Advance(CandleSnapshot candle, int newExecutions)
    {
        ArgumentNullException.ThrowIfNull(candle);

        if (candle.Account.StartingCapital != _startingCapital)
        {
            throw new ArgumentException("Metrics require the same frozen capital.", nameof(candle));
        }

        return Advance(
            new CandleMetricObservation(
                candle.Sequence,
                candle.Context.CandleIndex,
                candle.Account.StartingCapital,
                candle.Equity,
                candle.UnrealizedPnl,
                candle.Account.RealizedPnl,
                candle.Account.FeesBase,
                candle.Account.FeesQuote),
            newExecutions);
    }

    public MetricsAccumulatorState Advance(CandleMetricObservation candle, int newExecutions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newExecutions);

        if (candle.StartingCapital != _startingCapital)
        {
            throw new ArgumentException("Metrics require the same frozen capital.", nameof(candle));
        }

        if (candle.Sequence != ObservedCandles + 1L || candle.CandleIndex != ObservedCandles || ObservedCandles >= _ranges.TotalBars || candle.Equity < 0m)
        {
            throw new ArgumentException("Metrics require the next actual replay Close observation.", nameof(candle));
        }

        MetricsSegmentState full = _full.Advance(candle, newExecutions);
        MetricsSegmentState inSample = _inSample;
        MetricsSegmentState? outOfSample = _outOfSample;

        if (ObservedCandles < _ranges.InSampleBars)
        {
            inSample = inSample.Advance(candle, newExecutions);
        }
        else
        {
            outOfSample = outOfSample ?? throw new InvalidOperationException("Missing holdout segment.");

            if (outOfSample.Value.ObservedCandles == 0)
            {
                outOfSample = outOfSample.Value.Start(_full.EndEquity, _full.EndRealized, _full.EndUnrealized, _full.EndFeesBase, _full.EndFeesQuote);
            }

            outOfSample = outOfSample.Value.Advance(candle, newExecutions);
        }

        return new MetricsAccumulatorState(_startingCapital, _ranges, full, inSample, outOfSample, candle.CandleIndex);
    }

    private BacktestMetricsSnapshot Capture(int? candleIndex)
    {
        var diagnostics = new List<BacktestDiagnostic>();
        SampleMetrics full = Materialize(_full, "Full", diagnostics, candleIndex);
        SampleMetrics inSample = Materialize(_inSample, "InSample", diagnostics, candleIndex);
        SampleMetrics? outOfSample = _outOfSample is null ? null : Materialize(_outOfSample.Value, "OutOfSample", diagnostics, candleIndex);

        return new BacktestMetricsSnapshot(full, inSample, outOfSample, diagnostics);
    }

    private SampleMetrics Materialize(MetricsSegmentState state, string name, List<BacktestDiagnostic> diagnostics, int? candleIndex)
    {
        if (state.ObservedCandles == 0)
        {
            return new SampleMetrics(
                state.StartIndex,
                state.EndExclusive,
                ObservedCandles: 0,
                state.BoundaryEquity,
                EndEquity: null,
                TotalPnl: null,
                RealizedPnl: null,
                UnrealizedPnl: null,
                ReturnPercent: null,
                FeesBase: null,
                FeesQuote: null,
                MaxDrawdownPercent: null,
                Executions: 0);
        }

        decimal total = state.EndEquity - state.BoundaryEquity!.Value;
        decimal startingCapital = _startingCapital;

        decimal? Calculate(Func<decimal> operation, string field)
        {
            try
            {
                return operation();
            }
            catch (OverflowException)
            {
                diagnostics.Add(new BacktestDiagnostic("MetricsOverflow", $"{name}.{field} is not representable as decimal.", CandleIndex: candleIndex));

                return null;
            }
        }

        return new SampleMetrics(
            state.StartIndex,
            state.EndExclusive,
            state.ObservedCandles,
            state.BoundaryEquity,
            state.EndEquity,
            total,
            Calculate(() => state.EndRealized - state.BoundaryRealized, "RealizedPnl"),
            Calculate(() => state.EndUnrealized - state.BoundaryUnrealized, "UnrealizedPnl"),
            Calculate(() => total / startingCapital * 100m, "ReturnPercent"),
            Calculate(() => state.EndFeesBase - state.BoundaryFeesBase, "FeesBase"),
            Calculate(() => state.EndFeesQuote - state.BoundaryFeesQuote, "FeesQuote"),
            state.MaxDrawdownPercent,
            state.Executions);
    }
}