namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestChartInternal(int BucketSize, IReadOnlyList<BacktestChartPointInternal> Points);