using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetVenueBotPositions;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Lists the core's T-Invest bots with their positions and keeps those on the context's share whose credential names the
///     same account, so bots under different tokens of one account are added up too. Each other bot's account comes from its
///     own credential, read afresh; nothing is cached or logged.
/// </summary>
internal sealed class TInvestBotPositions(IServiceScopeFactory scopes, ITInvestBotContexts contexts) : ITInvestBotPositions
{
    public async Task<IReadOnlyList<VenueBotPositionInternal>> OnAccountShare(TInvestBotContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<VenueBotPositionInternal> all;

        using (IServiceScope scope = scopes.CreateScope())
        {
            all = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetVenueBotPositionsQuery(TInvestVenue.VenueId.Value), cancellationToken);
        }

        List<VenueBotPositionInternal> onAccount = [];

        foreach (VenueBotPositionInternal bot in all)
        {
            if (!TInvestSymbol.TryParse(bot.Symbol, out TInvestSymbol? symbol) || symbol != context.Share.Symbol)
            {
                continue;
            }

            string accountId = bot.BotId == context.BotId.Value ? context.AccountId : (await contexts.Get(new BotId(bot.BotId), cancellationToken)).AccountId;

            if (accountId == context.AccountId)
            {
                onAccount.Add(bot);
            }
        }

        return onAccount;
    }
}