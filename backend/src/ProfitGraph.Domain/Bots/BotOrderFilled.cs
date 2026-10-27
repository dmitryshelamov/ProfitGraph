using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The venue executed one of the bot's orders completely (ADR 0042); one event per order, whatever number of fills it took.
/// </summary>
public sealed record BotOrderFilled(BotId BotId, BotOrderFill Fill, DateTimeOffset At) : IDomainEvent;