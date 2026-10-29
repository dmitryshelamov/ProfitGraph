using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.CancelBotOrder;

/// <summary>
///     The owner cancels one working order of a running bot (ADR 0048) or of a stopped one (ADR 0055). A running bot's runner takes
///     the request as a root within a minute or it expires, and the engine cancels the order as it cancels its own; a bot has at
///     most one pending. A stopped bot's cancel is requested at once and goes to the venue as a cancel intent.
/// </summary>
public sealed record CancelBotOrderCommand(Guid OwnerId, Guid BotId, long EngineOrderId) : ICommand<OrderCancelInternal>;