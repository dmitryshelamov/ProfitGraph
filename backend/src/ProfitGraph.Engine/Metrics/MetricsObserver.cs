using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Per-run metrics observer consuming completed-candle scalars and committed executions.
/// </summary>
public sealed class MetricsObserver : IBacktestMetricsObserver
{
    private MetricsAccumulatorState _state;
    private BacktestMetricsSnapshot? _snapshot;
    private int _newExecutions;

    public BacktestMetricsSnapshot Snapshot => _snapshot ??= _state.Snapshot;

    public MetricsObserver(decimal startingCapital, SampleRanges ranges)
    {
        _state = new MetricsAccumulatorState(startingCapital, ranges);
    }

    public void OnLedger(BacktestLedgerEntry entry)
    {
        if (entry.Kind == BacktestLedgerKind.Filled)
        {
            _newExecutions = checked(_newExecutions + 1);
        }
    }

    public void OnCandleMetrics(CandleMetricObservation observation)
    {
        _state = _state.Advance(observation, _newExecutions);
        _snapshot = null;
        _newExecutions = 0;
    }

    public void OnCandleProcessed(CandleSnapshot snapshot)
    {
        _state = _state.Advance(snapshot, _newExecutions);
        _snapshot = null;
        _newExecutions = 0;
    }

    public void OnDiagnostic(BacktestDiagnostic diagnostic)
    {
    }

    public void OnTerminal(BacktestTerminalSnapshot terminal)
    {
    }
}