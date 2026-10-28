using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     The venue's gateway closed an order the venue no longer knows without the owner's confirmation, because every check of
///     its evidence held (ADR 0030). <see cref="Checks" /> are the stable codes of those checks; they hold no account data.
/// </summary>
public sealed record OrderIntentAbsenceProven(BotId BotId, OrderIntentId IntentId, long EngineOrderId, IReadOnlyList<string> Checks, DateTimeOffset At) : IDomainEvent;
