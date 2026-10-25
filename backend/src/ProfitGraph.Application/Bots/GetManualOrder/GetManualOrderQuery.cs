using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetManualOrder;

public sealed record GetManualOrderQuery(Guid OwnerId, Guid BotId, Guid Id) : IQuery<ManualOrderInternal>;