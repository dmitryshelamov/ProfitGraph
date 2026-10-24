using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot's engine now runs the given strategy version.
/// </summary>
public sealed record BotGraphVersionApplied(BotId BotId, StrategyVersionId StrategyVersionId, DateTimeOffset At) : IDomainEvent;