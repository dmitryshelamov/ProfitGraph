using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     A submit whose delivery to the venue is unknown, or an order the venue accepted and the bot can no longer account for,
///     with the engine's order and whether the owner may confirm it absent now. <see cref="AcceptedByVenue" /> tells the
///     second case: the owner then confirms that the venue removed the order and nothing was executed.
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
    DateTimeOffset? ConfirmedAbsentAt,
    bool AcceptedByVenue);