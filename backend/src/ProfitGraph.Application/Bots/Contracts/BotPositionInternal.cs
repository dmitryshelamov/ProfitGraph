using ProfitGraph.Domain.Bots.MarkPrices;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The bot's position from its checkpoint, valued at its display price (S5-19), with its recent cycles newest first.
///     The display price is null before the bot's first candle. The cost basis and the average cost hold the entry fees, as
///     strategies see them; the realized and unrealized profit show the entry fees as spent when they were paid, so the
///     average buy price and the unrealized profit leave them out (ADR 0063). Position fees are every fee of the open cycle.
/// </summary>
public sealed record BotPositionInternal(
    decimal BaseTotal,
    decimal QuoteTotal,
    decimal CostBasis,
    decimal EntryFees,
    decimal? AverageCost,
    decimal? AverageBuyPrice,
    decimal RealizedPnl,
    DateTimeOffset? OpenedAt,
    decimal? MarkPrice,
    DateTimeOffset? MarkPriceObservedAt,
    MarkPriceSource? MarkPriceSource,
    decimal? UnrealizedPnl,
    decimal? PositionFees,
    IReadOnlyList<BotPositionCycleInternal> Cycles);