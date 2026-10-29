namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

internal sealed class BacktestFacetDb
{
    public string Dimension { get; init; } = null!;
    public string? Value { get; init; }
    public string? Name { get; init; }
    public int Count { get; init; }
}