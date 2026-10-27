using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The venue restated the last closed candle the bot had processed before it restarted, and the bot continued on the
///     restated one (ADR 0029).
/// </summary>
public sealed record BotHistoryRestated(BotId BotId, DateTimeOffset OpenTime, decimal PreviousClose, decimal RestatedClose, DateTimeOffset At) : IDomainEvent;
