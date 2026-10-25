using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotWaitingOrders;

/// <summary>
///     The owner's bot's working orders that its venue holds locally, with why (ADR 0023, TC4).
/// </summary>
public sealed record GetBotWaitingOrdersQuery(Guid OwnerId, Guid BotId) : IQuery<IReadOnlyList<BotOrderWaitingInternal>>;