namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class TradingHaltDb
{
    public bool Halted { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}