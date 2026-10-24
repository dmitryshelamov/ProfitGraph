namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestChartResponse(int BucketSize, IReadOnlyList<BacktestChartPointResponse> Points);