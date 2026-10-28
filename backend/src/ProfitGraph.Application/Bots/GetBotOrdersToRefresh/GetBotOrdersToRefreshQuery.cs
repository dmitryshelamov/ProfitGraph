using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotOrdersToRefresh;

/// <summary>
///     What a venue reads again after the bot's private order stream was restored (ADR 0044): the orders of the bot's engine
///     after its last committed root and of its intents whose venue state is not known, as reconciliation asks for them. Null
///     for a bot that no longer exists or a venue without a gateway.
/// </summary>
public sealed record GetBotOrdersToRefreshQuery(Guid BotId) : IQuery<BotReconcileInternal?>;