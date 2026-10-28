using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderWaiting;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What the module tells the bot runtime about orders it holds locally (ADR 0023, TC4): why one waits, or that it no
///     longer does, whether the bot may send one now, and whether its day orders may be carried over without a runner.
/// </summary>
internal interface ITInvestOrderWaits
{
    Task Report(BotId botId, long engineOrderId, OrderWaitingCode? code, decimal? low, decimal? high, CancellationToken cancellationToken);

    Task<bool> MayRelease(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether the module may carry the bot's day orders over although no runner in this process reads their updates
    ///     (ADR 0030).
    /// </summary>
    Task<bool> MayCarryOverOffline(BotId botId, CancellationToken cancellationToken);
}