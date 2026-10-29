using ProfitGraph.Domain.Strategies;
using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Loads the bot, its market, its instrument rules and its decrypted credential for one call, and for a USDT-perpetual bot its
///     strategy's direction and its leverage; nothing is cached or logged.
/// </summary>
internal sealed class BybitBotContexts(IServiceScopeFactory scopes)
{
    public async Task<BybitBotContext> Get(BotId botId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        Bot bot = await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(botId, cancellationToken) ?? throw new BybitOrderUnresolvedException();
        BybitMarket market = BybitMarket.Of(bot.Series.Venue);
        IInstrumentRulesSource rules = scope.ServiceProvider.GetServices<IInstrumentRulesSource>().First(source => source.Venue == market.Venue);
        ExchangeCredentialSecretsInternal credential = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReadExchangeCredentialSecretsQuery(bot.OwnerId.Value, bot.CredentialId.Value), cancellationToken);
        var context = new BybitBotContext(botId, bot.Series.Symbol.Value, await rules.Get(bot.Series.Symbol, cancellationToken), credential) { Market = market };

        if (!market.IsLinear)
        {
            return context;
        }

        // A futures bot's position side follows its strategy's direction, which a running bot never changes (ADR 0061).
        StrategyVersion version = (await scope.ServiceProvider.GetRequiredService<IStrategyRepository>().GetVersions(bot.StrategyId, cancellationToken))
            .SingleOrDefault(candidate => candidate.Id == bot.StrategyVersionId) ?? throw new BybitOrderUnresolvedException();

        return context with { Direction = version.Definition.Direction, Leverage = BybitLinearBotSettingsSchema.Parse(bot.VenueSettings) };
    }
}
