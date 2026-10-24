using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Collects metrics and deterministic bucket-ending close equity for one walk-forward test range.
/// </summary>
public sealed class WalkForwardEquityObserver : IBacktestMetricsObserver
{
    private const int MaximumPoints = 2000;

    private readonly MetricsObserver _metrics;
    private readonly int _testStart;
    private readonly int _testCount;
    private readonly int _pointCount;
    private readonly IReadOnlyList<Candle> _replayCandles;
    private readonly List<WalkForwardEquityPoint> _equity;

    public BacktestMetricsSnapshot Snapshot => _metrics.Snapshot;
    public IReadOnlyList<WalkForwardEquityPoint> Equity { get; }

    public WalkForwardEquityObserver(decimal startingCapital, SampleRanges ranges, int testStart, IReadOnlyList<Candle> replayCandles)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(replayCandles);
        ArgumentOutOfRangeException.ThrowIfNegative(testStart);
        int testCount = replayCandles.Count;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(testCount);

        if (ranges.InSampleBars != testCount || ranges.OutOfSampleBars != 0 || (long)testStart + testCount > int.MaxValue)
        {
            throw new ArgumentException("Supply the complete test range without a holdout.", nameof(ranges));
        }

        _metrics = new MetricsObserver(startingCapital, ranges);
        _testStart = testStart;
        _testCount = testCount;
        _pointCount = Math.Min(testCount, MaximumPoints);
        _replayCandles = replayCandles;
        _equity = new List<WalkForwardEquityPoint>(_pointCount);
        Equity = _equity.AsReadOnly();
    }

    public void OnLedger(BacktestLedgerEntry entry)
    {
        _metrics.OnLedger(entry);
    }

    public void OnCandleMetrics(CandleMetricObservation observation)
    {
        _metrics.OnCandleMetrics(observation);
        Capture(observation.CandleIndex, observation.Equity);
    }

    public void OnCandleProcessed(CandleSnapshot snapshot)
    {
        _metrics.OnCandleProcessed(snapshot);
        Capture(snapshot.Context.CandleIndex, snapshot.Equity);
    }

    public void OnDiagnostic(BacktestDiagnostic diagnostic)
    {
        _metrics.OnDiagnostic(diagnostic);
    }

    public void OnTerminal(BacktestTerminalSnapshot terminal)
    {
        _metrics.OnTerminal(terminal);
    }

    private void Capture(int localIndex, decimal closeEquity)
    {
        int bucketEnd = checked((int)((long)(_equity.Count + 1) * _testCount / _pointCount) - 1);

        if (localIndex == bucketEnd)
        {
            _equity.Add(new WalkForwardEquityPoint(checked(_testStart + localIndex), closeEquity, _replayCandles[localIndex].OpenTime));
        }
    }
}