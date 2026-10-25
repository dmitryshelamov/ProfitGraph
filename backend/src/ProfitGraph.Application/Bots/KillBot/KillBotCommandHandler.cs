using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.KillBot;

internal sealed class KillBotCommandHandler(IBotRepository bots, BotKillSwitch killSwitch, TimeProvider clock) : IRequestHandler<KillBotCommand, BotInternal>
{
    public async Task<BotInternal> Handle(KillBotCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwnedForUpdate(command.Id, command.OwnerId, cancellationToken);
        bot.Kill(BotStatusReason.Killed, clock.GetUtcNow());
        await killSwitch.Apply(bot.Id, engine: null, cancellationToken);
        await bots.Update(bot, cancellationToken);

        return bot.ToInternal();
    }
}