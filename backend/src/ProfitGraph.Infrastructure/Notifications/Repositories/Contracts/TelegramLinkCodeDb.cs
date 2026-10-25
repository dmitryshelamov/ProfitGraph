namespace ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;

internal sealed class TelegramLinkCodeDb
{
    public string CodeHash { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}