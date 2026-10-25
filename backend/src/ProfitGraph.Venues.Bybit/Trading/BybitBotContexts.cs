using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Loads the bot, its instrument rules and its decrypted credential for one call; nothing is cached or logged.
/// </summary>
internal sealed class BybitBotContexts(IServiceScopeFactory scopes)
{
    public async Task<BybitBotContext> Get(BotId botId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        Bot bot = await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(botId, cancellationToken) ?? throw new BybitOrderUnresolvedException();
        IInstrumentRulesSource rules = scope.ServiceProvider.GetServices<IInstrumentRulesSource>().First(source => source.Venue == BybitVenue.VenueId);
        ExchangeCredentialSecretsInternal credential = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReadExchangeCredentialSecretsQuery(bot.OwnerId.Value, bot.CredentialId.Value), cancellationToken);

        return new BybitBotContext(botId, bot.Series.Symbol.Value, await rules.Get(bot.Series.Symbol, cancellationToken), credential);
    }
}
