namespace ProfitGraph.Infrastructure.MarketData.Repositories.Contracts;

internal sealed class CandleClosureDb
{
    public DateTimeOffset FromTime { get; init; }
    public DateTimeOffset ToTime { get; init; }
}