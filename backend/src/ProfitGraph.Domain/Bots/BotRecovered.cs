using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot runs again after a degradation or failed restarts.
/// </summary>
public sealed record BotRecovered(BotId BotId, DateTimeOffset At) : IDomainEvent;