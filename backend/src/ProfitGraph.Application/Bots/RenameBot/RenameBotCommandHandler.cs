using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RenameBot;

internal sealed class RenameBotCommandHandler(IBotRepository bots, TimeProvider clock) : IRequestHandler<RenameBotCommand, BotInternal>
{
    public async Task<BotInternal> Handle(RenameBotCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwnedForUpdate(command.Id, command.OwnerId, cancellationToken);
        BotName? name = string.IsNullOrWhiteSpace(command.Name) ? null : new BotName(command.Name);

        if (bot.Rename(name, clock.GetUtcNow()))
        {
            await bots.Update(bot, cancellationToken);
        }

        return bot.ToInternal();
    }
}