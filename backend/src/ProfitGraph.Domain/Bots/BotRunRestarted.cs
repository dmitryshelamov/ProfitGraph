using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A stopped run that never created an order was forgotten, so the next start begins a new run.
/// </summary>
public sealed record BotRunRestarted(BotId BotId, DateTimeOffset At) : IDomainEvent;