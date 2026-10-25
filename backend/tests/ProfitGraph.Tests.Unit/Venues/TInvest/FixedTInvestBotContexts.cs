using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

internal sealed class FixedTInvestBotContexts(TInvestBotContext context) : ITInvestBotContexts
{
    public Task<TInvestBotContext> Get(BotId botId, CancellationToken cancellationToken)
    {
        return Task.FromResult(context with { BotId = botId });
    }
}