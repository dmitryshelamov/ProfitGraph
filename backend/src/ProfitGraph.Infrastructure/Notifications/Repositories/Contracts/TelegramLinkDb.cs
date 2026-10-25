namespace ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;

internal sealed class TelegramLinkDb
{
    public Guid UserId { get; init; }
    public long ChatId { get; init; }
    public DateTimeOffset LinkedAt { get; init; }
    public DateTimeOffset? BlockedAt { get; init; }
}