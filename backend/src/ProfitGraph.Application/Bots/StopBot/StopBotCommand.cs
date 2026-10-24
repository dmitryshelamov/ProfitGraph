using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.StopBot;

public sealed record StopBotCommand(Guid OwnerId, Guid Id) : ICommand<BotInternal>;