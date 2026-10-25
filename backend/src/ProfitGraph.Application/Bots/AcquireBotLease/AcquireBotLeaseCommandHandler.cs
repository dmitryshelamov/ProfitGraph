using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.AcquireBotLease;

internal sealed class AcquireBotLeaseCommandHandler(IBotRepository bots, IBotLeaseRepository leases, TimeProvider clock) : IRequestHandler<AcquireBotLeaseCommand, long?>
{
    public async Task<long?> Handle(AcquireBotLeaseCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var botId = new BotId(command.BotId);
        var holder = new LeaseHolderId(command.HolderId);

        // Rows are locked lease, bot, intent everywhere (ADR 0016), so two writers never wait on each other in a cycle.
        BotLease? lease = await leases.GetForUpdate(botId, cancellationToken);
        Bot? bot = await bots.GetForUpdate(botId, cancellationToken);

        if (bot is null || !bot.CanAcquireLease(now))
        {
            return null;
        }

        if (lease is null)
        {
            lease = BotLease.Acquire(botId, holder, now, command.Duration);
            await leases.Add(lease, cancellationToken);
        }
        else if (lease.IsExpired(now))
        {
            lease.TakeOver(holder, now, command.Duration);
            await leases.Update(lease, cancellationToken);
        }
        else
        {
            return null;
        }

        bot.RecordLeaseAcquired(now);
        await bots.Update(bot, cancellationToken);

        return lease.Epoch;
    }
}