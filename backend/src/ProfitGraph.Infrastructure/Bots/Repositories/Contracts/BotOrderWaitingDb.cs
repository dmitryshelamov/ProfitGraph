namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotOrderWaitingDb
{
    public Guid BotId { get; init; }
    public long EngineOrderId { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTimeOffset Since { get; init; }
    public DateTimeOffset? Until { get; init; }
    public decimal? Low { get; init; }
    public decimal? High { get; init; }
}