using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A submit whose delivery to the venue is unknown; amounts are exact invariant decimal strings. Confirm it absent only
///     after checking the account on the venue, when <see cref="CanConfirmAbsent" /> is true.
/// </summary>
public sealed record BotUnresolvedOrderResponse(
    Guid IntentId,
    long EngineOrderId,
    OrderSide Side,
    OrderType Type,
    string? Quantity,
    string? QuoteBudget,
    string? LimitPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlaceStartedAt,
    bool CanConfirmAbsent,
    DateTimeOffset? ConfirmedAbsentAt);