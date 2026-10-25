namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class LiveMarkPriceDb
{
    public string Venue { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
}