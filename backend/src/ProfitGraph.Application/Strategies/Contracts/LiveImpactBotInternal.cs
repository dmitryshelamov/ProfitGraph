using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Strategies.Contracts;

/// <summary>
///     A running bot that switches to a newly saved version of its strategy on its next root. <see cref="Parameters" /> are its
///     values; a parameter the new version adds takes its default and a removed one is dropped (ADR 0047).
/// </summary>
public sealed record LiveImpactBotInternal(
    Guid BotId,
    string Venue,
    string Symbol,
    string Timeframe,
    Guid StrategyVersionId,
    BotRuntimeStatus RuntimeStatus,
    IReadOnlyDictionary<string, decimal> Parameters);