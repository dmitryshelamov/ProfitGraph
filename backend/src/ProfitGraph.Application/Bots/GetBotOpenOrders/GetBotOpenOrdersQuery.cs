using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotOpenOrders;

/// <summary>
///     The owner's bot's open orders as its engine holds them after the last committed root: accepted, awaiting submission
///     or awaiting cancellation. A stopped bot's orders also follow its cancel intents (ADR 0055).
/// </summary>
public sealed record GetBotOpenOrdersQuery(Guid OwnerId, Guid BotId) : IQuery<IReadOnlyList<BotOpenOrderInternal>>;
