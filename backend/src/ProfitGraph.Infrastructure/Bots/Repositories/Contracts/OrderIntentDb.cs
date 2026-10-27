namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class OrderIntentDb
{
    public Guid Id { get; init; }
    public Guid BotId { get; init; }
    public long EngineOrderId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string ClientOrderId { get; init; } = string.Empty;
    public string? Payload { get; init; }
    public bool FromKill { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public DateTimeOffset? PlaceStartedAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public string? RejectionReason { get; init; }
    public string? RejectionMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ConfirmedAbsentAt { get; init; }
    public Guid? ConfirmedAbsentBy { get; init; }
    public bool ConfirmedAbsentAutomatically { get; init; }
    public string? DeferralCode { get; init; }
    public DateTimeOffset? DeferralSince { get; init; }
    public DateTimeOffset? DeferralUntil { get; init; }
    public decimal? DeferralLow { get; init; }
    public decimal? DeferralHigh { get; init; }
}