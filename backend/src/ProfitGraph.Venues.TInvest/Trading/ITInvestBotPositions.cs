using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     What the bots that trade the bot's share through the same T-Invest account hold, by their last checkpoints (ADR 0031).
/// </summary>
internal interface ITInvestBotPositions
{
    /// <summary>
    ///     The positions of every bot on the context's account and share, the context's own bot included. Throws when an
    ///     account cannot be told, so the caller never adds up a partial list.
    /// </summary>
    Task<IReadOnlyList<VenueBotPositionInternal>> OnAccountShare(TInvestBotContext context, CancellationToken cancellationToken);
}