using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Domain.Bots.OrderWaiting;

namespace ProfitGraph.Application.Bots.RecordOrderWaiting;

/// <summary>
///     A venue module reports why it holds a bot's working order locally, or with a null <see cref="Code" /> that it no
///     longer does (ADR 0023, TC4).
/// </summary>
public sealed record RecordOrderWaitingCommand(
    Guid BotId,
    long EngineOrderId,
    OrderWaitingCode? Code,
    DateTimeOffset? Until = null,
    decimal? Low = null,
    decimal? High = null) : ICommand;