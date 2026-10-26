using ProfitGraph.Domain.Accounts;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Every stored field of an order intent, for restoring it from storage and for writing it back.
/// </summary>
public sealed record OrderIntentSnapshot(
    OrderIntentId Id,
    BotId BotId,
    long EngineOrderId,
    OrderIntentKind Kind,
    ClientOrderId ClientOrderId,
    OrderSnapshot? Order,
    bool FromKill,
    OrderIntentStatus Status,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? PlaceStartedAt,
    DateTimeOffset? SentAt,
    OrderIntentRejection? Rejection,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAbsentAt = null,
    UserId? ConfirmedAbsentBy = null);