using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestMetricsInternal(SampleMetrics Full, SampleMetrics InSample, SampleMetrics? OutOfSample);