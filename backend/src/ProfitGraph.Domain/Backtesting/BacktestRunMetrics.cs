using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     Final metrics of the whole replay, the in-sample part and the holdout (ADR 0018 of Profit Fox Designer).
/// </summary>
public sealed record BacktestRunMetrics(SampleMetrics Full, SampleMetrics InSample, SampleMetrics? OutOfSample);