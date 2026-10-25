using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Strategies.Contracts;

/// <summary>
///     A running bot that switches to a newly saved version of its strategy on its next root.
/// </summary>
public sealed record LiveImpactBotInternal(Guid BotId, string Venue, string Symbol, string Timeframe, Guid StrategyVersionId, BotRuntimeStatus RuntimeStatus);