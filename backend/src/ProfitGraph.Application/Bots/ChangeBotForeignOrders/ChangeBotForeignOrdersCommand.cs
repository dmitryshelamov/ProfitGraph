using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ChangeBotForeignOrders;

public sealed record ChangeBotForeignOrdersCommand(Guid OwnerId, Guid Id, bool AllowForeignOrders) : ICommand<BotInternal>;