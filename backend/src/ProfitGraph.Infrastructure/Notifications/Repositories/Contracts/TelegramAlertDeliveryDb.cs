namespace ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;

internal sealed class TelegramAlertDeliveryDb
{
    public Guid AlertId { get; init; }
    public long ChatId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
}