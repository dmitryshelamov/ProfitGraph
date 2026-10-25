namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The bot's position from its checkpoint, with its recent cycles newest first.
/// </summary>
public sealed record BotPositionInternal(
    decimal BaseTotal,
    decimal QuoteTotal,
    decimal CostBasis,
    decimal? AverageCost,
    decimal RealizedPnl,
    DateTimeOffset? OpenedAt,
    IReadOnlyList<BotPositionCycleInternal> Cycles);