using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The bot's position at its display price and its recent cycles; decimals are exact invariant strings.
/// </summary>
public sealed record BotPositionResponse(
    string BaseTotal,
    string QuoteTotal,
    string CostBasis,
    string? AverageCost,
    string RealizedPnl,
    DateTimeOffset? OpenedAt,
    string? MarkPrice,
    DateTimeOffset? MarkPriceObservedAt,
    MarkPriceSource? MarkPriceSource,
    string? UnrealizedPnl,
    IReadOnlyList<BotPositionCycleResponse> Cycles);