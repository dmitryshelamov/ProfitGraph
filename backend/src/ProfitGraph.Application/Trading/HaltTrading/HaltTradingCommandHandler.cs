using MediatR;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Trading.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Trading.HaltTrading;

internal sealed class HaltTradingCommandHandler(ITradingHaltRepository halts, IBotRepository bots, BotKillSwitch killSwitch, TimeProvider clock)
    : IRequestHandler<HaltTradingCommand, TradingHaltInternal>
{
    public async Task<TradingHaltInternal> Handle(HaltTradingCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        TradingHalt halt = await halts.GetForUpdate(cancellationToken);

        if (halt.Halt(now))
        {
            await halts.Update(halt, cancellationToken);
        }

        foreach (BotId id in await bots.ListDesiredRunning(cancellationToken))
        {
            Bot? bot = await bots.GetForUpdate(id, cancellationToken);

            if (bot is not { DesiredState: BotDesiredState.Running })
            {
                continue;
            }

            bot.Kill(BotStatusReason.Killed, now);
            await killSwitch.Apply(bot.Id, engine: null, cancellationToken);
            await bots.Update(bot, cancellationToken);
        }

        return new TradingHaltInternal(halt.IsHalted, halt.UpdatedAt);
    }
}