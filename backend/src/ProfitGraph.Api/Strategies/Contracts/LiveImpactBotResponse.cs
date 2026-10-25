using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record LiveImpactBotResponse(Guid BotId, string Venue, string Symbol, string Timeframe, Guid StrategyVersionId, BotRuntimeStatus RuntimeStatus);