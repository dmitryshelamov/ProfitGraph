using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.AuthorizeOfflineCarryOver;

/// <summary>
///     Reads the lease and the bot under their locks and the halt, as a place start does. A bot whose runner failed and waits
///     for its automatic restart (<see cref="BotStatusReason.RuntimeFailure" />), or whose runtime died and lost its lease,
///     qualifies; a bot degraded for a safety reason, killed, halted or stopped by the owner does not.
/// </summary>
internal sealed class AuthorizeOfflineCarryOverCommandHandler(
    IBotLeaseRepository leases,
    IBotRepository bots,
    ITradingHaltRepository halts,
    TimeProvider clock) : IRequestHandler<AuthorizeOfflineCarryOverCommand, bool>
{
    public async Task<bool> Handle(AuthorizeOfflineCarryOverCommand command, CancellationToken cancellationToken)
    {
        var botId = new BotId(command.BotId);
        BotLease? lease = await leases.GetForUpdate(botId, cancellationToken);
        Bot? bot = await bots.GetForUpdate(botId, cancellationToken);
        TradingHalt halt = await halts.Get(cancellationToken);

        return bot is { DesiredState: BotDesiredState.Running } && !halt.IsHalted && (lease is null || lease.IsExpired(clock.GetUtcNow())) &&
            (!bot.IsDegraded || bot.StatusReason == BotStatusReason.RuntimeFailure);
    }
}
