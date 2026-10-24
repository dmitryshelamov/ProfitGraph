using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The global kill switch stopped every submit and every running bot.
/// </summary>
public sealed record TradingHalted(DateTimeOffset At) : IDomainEvent;