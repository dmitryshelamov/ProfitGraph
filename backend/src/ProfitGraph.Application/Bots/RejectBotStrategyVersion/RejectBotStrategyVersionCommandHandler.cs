using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Bots.RejectBotStrategyVersion;

internal sealed class RejectBotStrategyVersionCommandHandler(IBotRepository bots, IBotLeaseRepository leases, TimeProvider clock)
    : IRequestHandler<RejectBotStrategyVersionCommand, bool>
{
    public async Task<bool> Handle(RejectBotStrategyVersionCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var botId = new BotId(command.BotId);
        BotLease? lease = await leases.GetForUpdate(botId, cancellationToken);

        if (lease is null || !lease.IsHeldBy(new LeaseHolderId(command.HolderId), command.Epoch, now))
        {
            return false;
        }

        Bot bot = await bots.GetForUpdate(botId, cancellationToken) ?? throw new InvalidOperationException("A leased bot does not exist.");

        if (bot.RejectStrategyVersion(new StrategyVersionId(command.StrategyVersionId), now))
        {
            await bots.Update(bot, cancellationToken);
        }

        return true;
    }
}