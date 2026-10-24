using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The owner changed the stopped bot's parameter values or allocation cap.
/// </summary>
public sealed record BotParametersChanged(BotId BotId, DateTimeOffset At) : IDomainEvent;