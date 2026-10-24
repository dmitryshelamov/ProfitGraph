namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

internal sealed class BacktestChartSizesDb
{
    public int? Stored { get; init; }
    public int Live { get; init; }
}