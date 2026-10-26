using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotUnresolvedOrders;

/// <summary>
///     The owner's bot's submits whose delivery to the venue is unknown, and whether each may be confirmed absent now.
/// </summary>
public sealed record GetBotUnresolvedOrdersQuery(Guid OwnerId, Guid BotId) : IQuery<IReadOnlyList<BotUnresolvedOrderInternal>>;