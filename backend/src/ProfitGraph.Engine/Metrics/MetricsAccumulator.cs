using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Pure immutable completed-candle metrics; preparing a retained batch never mutates the committed accumulator.
/// </summary>
public sealed class MetricsAccumulator
{
    private readonly MetricsAccumulatorState _state;
    private BacktestMetricsSnapshot? _snapshot;

    public BacktestMetricsSnapshot Snapshot => _snapshot ??= _state.Snapshot;
    public int ObservedCandles => _state.ObservedCandles;

    public MetricsAccumulator(decimal startingCapital, SampleRanges ranges)
    {
        _state = new MetricsAccumulatorState(startingCapital, ranges);
    }

    private MetricsAccumulator(MetricsAccumulatorState state)
    {
        _state = state;
    }

    public MetricsAccumulator Advance(CandleSnapshot candle, int newExecutions)
    {
        return new MetricsAccumulator(_state.Advance(candle, newExecutions));
    }

    public MetricsAccumulator Advance(CandleMetricObservation candle, int newExecutions)
    {
        return new MetricsAccumulator(_state.Advance(candle, newExecutions));
    }
}