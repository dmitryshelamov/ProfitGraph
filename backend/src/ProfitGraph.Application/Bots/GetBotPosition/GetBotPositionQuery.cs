using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotPosition;

public sealed record GetBotPositionQuery(Guid OwnerId, Guid BotId, int CycleLimit) : IQuery<BotPositionInternal>;