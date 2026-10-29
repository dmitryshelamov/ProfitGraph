namespace ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;

internal sealed class TelegramNotificationDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public Guid? BotId { get; init; }
    public Guid? ScanPresetId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public Guid? AlertId { get; init; }
    public string Details { get; init; } = "{}";
    public DateTimeOffset CreatedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public DateTimeOffset NextAttemptAt { get; init; }
    public Guid? ClaimId { get; init; }
    public DateTimeOffset? ClaimedUntil { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public string? LastError { get; init; }
}