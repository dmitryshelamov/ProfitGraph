using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.CancelBotOrder;

/// <summary>
///     The owner cancels one working order of a running bot (ADR 0048). The bot's runner takes the request as a root within a
///     minute or it expires, and the engine cancels the order as it cancels its own; a bot has at most one pending.
/// </summary>
public sealed record CancelBotOrderCommand(Guid OwnerId, Guid BotId, long EngineOrderId) : ICommand<OrderCancelInternal>;