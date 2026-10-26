using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotOpenOrders;

/// <summary>
///     The owner's bot's open orders as its engine holds them after the last committed root: accepted, awaiting submission
///     or awaiting cancellation.
/// </summary>
public sealed record GetBotOpenOrdersQuery(Guid OwnerId, Guid BotId) : IQuery<IReadOnlyList<BotOpenOrderInternal>>;
