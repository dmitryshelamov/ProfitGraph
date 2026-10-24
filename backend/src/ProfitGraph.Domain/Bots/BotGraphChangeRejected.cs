using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The given saved strategy version cannot run in the bot; it keeps its previous version.
/// </summary>
public sealed record BotGraphChangeRejected(BotId BotId, StrategyVersionId StrategyVersionId, DateTimeOffset At) : IDomainEvent;