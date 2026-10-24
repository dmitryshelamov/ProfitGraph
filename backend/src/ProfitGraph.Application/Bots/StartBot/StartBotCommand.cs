using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.StartBot;

public sealed record StartBotCommand(Guid OwnerId, Guid Id) : ICommand<BotInternal>;