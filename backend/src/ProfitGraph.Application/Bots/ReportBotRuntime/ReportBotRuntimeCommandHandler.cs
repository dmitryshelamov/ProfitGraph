using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ReportBotRuntime;

internal sealed class ReportBotRuntimeCommandHandler(IBotRepository bots, IBotLeaseRepository leases, TimeProvider clock) : IRequestHandler<ReportBotRuntimeCommand, bool>
{
    public async Task<bool> Handle(ReportBotRuntimeCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var botId = new BotId(command.BotId);
        BotLease? lease = await leases.GetForUpdate(botId, cancellationToken);

        if (lease is null || !lease.IsHeldBy(new LeaseHolderId(command.HolderId), command.Epoch, now))
        {
            return false;
        }

        Bot bot = await bots.GetForUpdate(botId, cancellationToken) ?? throw new InvalidOperationException("A leased bot does not exist.");
        bool changed = command.Report switch
        {
            BotRuntimeReport.Starting => bot.MarkStarting(now),
            BotRuntimeReport.Running => bot.MarkRunning(now),
            BotRuntimeReport.Stopped => bot.MarkRuntimeStopped(now),
            BotRuntimeReport.Failed => bot.RecordRuntimeFailure(now),
            BotRuntimeReport.Degraded => bot.Degrade(command.Reason ?? throw new ArgumentException("A degradation needs a reason."), now),
            _ => throw new ArgumentOutOfRangeException(nameof(command), "Unknown runtime report.")
        };

        if (changed)
        {
            await bots.Update(bot, cancellationToken);
        }

        return true;
    }
}