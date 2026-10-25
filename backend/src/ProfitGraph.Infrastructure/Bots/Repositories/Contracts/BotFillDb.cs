namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotFillDb
{
    public Guid Id { get; init; }
    public long Sequence { get; init; }
    public Guid BotId { get; init; }
    public long EngineOrderId { get; init; }
    public string Side { get; init; } = string.Empty;
    public string? Tag { get; init; }
    public decimal GrossBase { get; init; }
    public decimal GrossQuote { get; init; }
    public decimal Fee { get; init; }
    public decimal CumulativeGrossBase { get; init; }
    public DateTimeOffset FilledAt { get; init; }
}