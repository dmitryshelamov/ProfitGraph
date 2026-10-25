using MediatR;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.AuthorizeWaitingRelease;

/// <summary>
///     Reads the lease and the bot under their locks and the halt in one transaction, as a place start does, so a kill in
///     progress is waited for.
/// </summary>
internal sealed class AuthorizeWaitingReleaseCommandHandler(
    IBotLeaseRepository leases,
    IBotRepository bots,
    ITradingHaltRepository halts,
    ITransactionExecutor transactions,
    TimeProvider clock) : IRequestHandler<AuthorizeWaitingReleaseCommand, bool>
{
    public Task<bool> Handle(AuthorizeWaitingReleaseCommand command, CancellationToken cancellationToken)
    {
        var botId = new BotId(command.BotId);

        return transactions.ExecuteAsync(
            async () =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                BotLease? lease = await leases.GetForUpdate(botId, cancellationToken);
                Bot? bot = await bots.GetForUpdate(botId, cancellationToken);
                TradingHalt halt = await halts.Get(cancellationToken);

                return bot is not null && bot.CanSubmit(halt.IsHalted) && lease is not null && !lease.IsExpired(now) && lease.SubmissionsReady;
            },
            cancellationToken);
    }
}