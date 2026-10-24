using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The venue became temporarily unavailable; submissions pause.
/// </summary>
public sealed record BotExchangeUnavailable(BotId BotId, DateTimeOffset At) : IDomainEvent;