using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ChangeBotForeignOrders;

internal sealed class ChangeBotForeignOrdersCommandHandler(IBotRepository bots, TimeProvider clock) : IRequestHandler<ChangeBotForeignOrdersCommand, BotInternal>
{
    public async Task<BotInternal> Handle(ChangeBotForeignOrdersCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwnedForUpdate(command.Id, command.OwnerId, cancellationToken);
        bot.ChangeForeignOrdersPolicy(command.AllowForeignOrders, clock.GetUtcNow());
        await bots.Update(bot, cancellationToken);

        return bot.ToInternal();
    }
}