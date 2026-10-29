namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanTradeDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public string Venue { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string Scenario { get; init; } = "";
    public string LevelsTimeframe { get; init; } = "";
    public decimal Entry { get; init; }
    public decimal Stop { get; init; }
    public decimal Target { get; init; }
    public decimal Amount { get; init; }
    public decimal Risk { get; init; }
    public Guid BotId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset? OpenedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string? Exit { get; init; }
    public decimal? RealizedPnl { get; init; }
    public string? FailureCode { get; init; }
    public bool BotReleased { get; init; }
    public string Direction { get; init; } = "Long";
}