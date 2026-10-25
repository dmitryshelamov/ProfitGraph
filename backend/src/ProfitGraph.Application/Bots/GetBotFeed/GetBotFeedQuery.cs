using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotFeed;

public sealed record GetBotFeedQuery(Guid OwnerId, Guid BotId, long? Before, int Limit) : IQuery<BotFeedPageInternal>;