using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     A submit whose delivery to the venue is unknown, with the engine's order and whether the owner may confirm it absent
///     now.
/// </summary>
public sealed record BotUnresolvedOrderInternal(
    Guid IntentId,
    long EngineOrderId,
    OrderSide Side,
    OrderType Type,
    decimal? Quantity,
    decimal? QuoteBudget,
    decimal? LimitPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlaceStartedAt,
    bool CanConfirmAbsent,
    DateTimeOffset? ConfirmedAbsentAt);