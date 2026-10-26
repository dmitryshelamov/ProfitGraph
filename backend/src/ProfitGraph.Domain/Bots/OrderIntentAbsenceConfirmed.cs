using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The owner confirmed that a submit whose delivery is unknown never reached the venue.
/// </summary>
public sealed record OrderIntentAbsenceConfirmed(BotId BotId, OrderIntentId IntentId, long EngineOrderId, UserId By, DateTimeOffset At) : IDomainEvent;