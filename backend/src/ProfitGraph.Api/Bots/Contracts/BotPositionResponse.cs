namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The bot's position and its recent cycles; decimals are exact invariant strings.
/// </summary>
public sealed record BotPositionResponse(
    string BaseTotal,
    string QuoteTotal,
    string CostBasis,
    string? AverageCost,
    string RealizedPnl,
    DateTimeOffset? OpenedAt,
    IReadOnlyList<BotPositionCycleResponse> Cycles);