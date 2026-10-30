using ProfitGraph.Domain.Bots.MarkPrices;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The bot's position at its display price and its recent cycles; decimals are exact invariant strings. The cost basis and
///     the average cost hold the entry fees; the realized and unrealized profit and the average buy price show them as spent
///     (ADR 0063). A short has a negative base total, and its average buy price is the average price it sold at (ADR 0061).
/// </summary>
public sealed record BotPositionResponse(
    string BaseTotal,
    string QuoteTotal,
    string CostBasis,
    string EntryFees,
    string? AverageCost,
    string? AverageBuyPrice,
    string RealizedPnl,
    DateTimeOffset? OpenedAt,
    string? MarkPrice,
    DateTimeOffset? MarkPriceObservedAt,
    MarkPriceSource? MarkPriceSource,
    string? UnrealizedPnl,
    string? PositionFees,
    IReadOnlyList<BotPositionCycleResponse> Cycles);