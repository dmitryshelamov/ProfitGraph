using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What the module tells the bot runtime about orders it holds locally (ADR 0023, TC4): why one waits, or that it no
///     longer does, and whether the bot may send one now.
/// </summary>
internal interface ITInvestOrderWaits
{
    Task Report(BotId botId, long engineOrderId, OrderWaitingCode? code, decimal? low, decimal? high, CancellationToken cancellationToken);

    Task<bool> MayRelease(BotId botId, CancellationToken cancellationToken);
}