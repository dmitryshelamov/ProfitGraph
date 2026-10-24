using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Copied metrics for completed candle observations, separate from reached terminal valuation.
/// </summary>
public sealed class BacktestMetricsSnapshot
{
    public SampleMetrics Full { get; }
    public SampleMetrics InSample { get; }
    public SampleMetrics? OutOfSample { get; }
    public IReadOnlyList<BacktestDiagnostic> Diagnostics { get; }

    public BacktestMetricsSnapshot(SampleMetrics full, SampleMetrics inSample, SampleMetrics? outOfSample, IEnumerable<BacktestDiagnostic> diagnostics)
    {
        Full = full;
        InSample = inSample;
        OutOfSample = outOfSample;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
}