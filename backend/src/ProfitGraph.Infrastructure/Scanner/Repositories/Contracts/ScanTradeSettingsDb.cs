namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanTradeSettingsDb
{
    public Guid OwnerId { get; init; }
    public string Venue { get; init; } = "";
    public Guid CredentialId { get; init; }
    public decimal Risk { get; init; }
    public decimal MaxAmount { get; init; }
    public Guid? StrategyId { get; init; }
    public Guid? ShortStrategyId { get; init; }
    public decimal? Leverage { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}