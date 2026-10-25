using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotFills;

public sealed record GetBotFillsQuery(Guid OwnerId, Guid BotId, long? Before, int Limit) : IQuery<BotFillPageInternal>;