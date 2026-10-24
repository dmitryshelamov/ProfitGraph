using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The global halt was cleared; bots can start again.
/// </summary>
public sealed record TradingResumed(DateTimeOffset At) : IDomainEvent;