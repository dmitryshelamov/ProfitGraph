using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Loads the bot, its decrypted credential and its share for one call; nothing is cached or logged.
/// </summary>
internal sealed class TInvestBotContexts(IServiceScopeFactory scopes, TInvestShares shares) : ITInvestBotContexts
{
    public async Task<TInvestBotContext> Get(BotId botId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        Bot bot = await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(botId, cancellationToken) ?? throw new TInvestOrderUnresolvedException();
        ExchangeCredentialSecretsInternal credential = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReadExchangeCredentialSecretsQuery(bot.OwnerId.Value, bot.CredentialId.Value), cancellationToken);
        var token = new TInvestToken(credential.Field(TInvestCredentialFields.Token));

        if (!TInvestSymbol.TryParse(bot.Series.Symbol.Value, out TInvestSymbol? symbol))
        {
            throw new TInvestOrderUnresolvedException();
        }

        return new TInvestBotContext(botId, token, credential.Field(TInvestCredentialFields.AccountId), await shares.Get(token, symbol, cancellationToken));
    }
}