namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestMetricsResponse(SampleMetricsResponse Full, SampleMetricsResponse InSample, SampleMetricsResponse? OutOfSample);