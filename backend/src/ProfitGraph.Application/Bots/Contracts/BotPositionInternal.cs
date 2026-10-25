using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The bot's position from its checkpoint, valued at its display price (S5-19), with its recent cycles newest first.
///     The display price is null before the bot's first candle.
/// </summary>
public sealed record BotPositionInternal(
    decimal BaseTotal,
    decimal QuoteTotal,
    decimal CostBasis,
    decimal? AverageCost,
    decimal RealizedPnl,
    DateTimeOffset? OpenedAt,
    decimal? MarkPrice,
    DateTimeOffset? MarkPriceObservedAt,
    MarkPriceSource? MarkPriceSource,
    decimal? UnrealizedPnl,
    IReadOnlyList<BotPositionCycleInternal> Cycles);