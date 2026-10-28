using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetOrderCancel;

public sealed record GetOrderCancelQuery(Guid OwnerId, Guid BotId, Guid Id) : IQuery<OrderCancelInternal>;