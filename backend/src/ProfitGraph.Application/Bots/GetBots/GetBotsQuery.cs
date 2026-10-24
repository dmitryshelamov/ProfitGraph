using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBots;

public sealed record GetBotsQuery(Guid OwnerId) : IQuery<IReadOnlyList<BotInternal>>;