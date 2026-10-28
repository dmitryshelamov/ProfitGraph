using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Trading;

namespace ProfitGraph.Application.Bots.AuthorizeWaitingRelease;

/// <summary>
///     Reads the lease and the bot under their locks and the halt, as a place start does, so a kill in progress is waited
///     for. Like every command it runs in one transaction (the transaction behavior).
/// </summary>
internal sealed class AuthorizeWaitingReleaseCommandHandler(
    IBotLeaseRepository leases,
    IBotRepository bots,
    ITradingHaltRepository halts,
    TimeProvider clock) : IRequestHandler<AuthorizeWaitingReleaseCommand, bool>
{
    public async Task<bool> Handle(AuthorizeWaitingReleaseCommand command, CancellationToken cancellationToken)
    {
        var botId = new BotId(command.BotId);
        DateTimeOffset now = clock.GetUtcNow();
        BotLease? lease = await leases.GetForUpdate(botId, cancellationToken);
        Bot? bot = await bots.GetForUpdate(botId, cancellationToken);
        TradingHalt halt = await halts.Get(cancellationToken);

        return bot is not null && bot.CanSubmit(halt.IsHalted) && lease is not null && !lease.IsExpired(now) && lease.SubmissionsReady;
    }
}