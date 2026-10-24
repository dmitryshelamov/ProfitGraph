using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.StopBot;

internal sealed class StopBotCommandHandler(IBotRepository bots, TimeProvider clock) : IRequestHandler<StopBotCommand, BotInternal>
{
    public async Task<BotInternal> Handle(StopBotCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwnedForUpdate(command.Id, command.OwnerId, cancellationToken);

        if (bot.Stop(clock.GetUtcNow()))
        {
            await bots.Update(bot, cancellationToken);
        }

        return bot.ToInternal();
    }
}