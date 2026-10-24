using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The owner started the bot; it runs the given strategy version.
/// </summary>
public sealed record BotStarted(BotId BotId, StrategyVersionId StrategyVersionId, DateTimeOffset At) : IDomainEvent;