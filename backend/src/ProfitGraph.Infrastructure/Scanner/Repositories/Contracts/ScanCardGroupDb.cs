namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanCardGroupDb
{
    public string Timeframe { get; init; } = string.Empty;
    public string Scenario { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public short Third { get; init; }
    public int Trades { get; init; }
    public decimal MeanR { get; init; }
}