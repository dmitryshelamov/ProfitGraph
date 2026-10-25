using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The token, account and share a T-Invest call for a bot uses, read afresh for each call.
/// </summary>
internal interface ITInvestBotContexts
{
    Task<TInvestBotContext> Get(BotId botId, CancellationToken cancellationToken);
}