using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBot;

public sealed record GetBotQuery(Guid OwnerId, Guid Id) : IQuery<BotInternal>;