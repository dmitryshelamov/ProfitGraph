using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.LiveData;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Persistence.Repositories;
using ProfitGraph.Venues.TInvest.Trading;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest;

public static class TInvestRegistration
{
    /// <summary>
    ///     Registers T-Invest MOEX shares (ADR 0023): the descriptor, the credential fields with their account selection, share
    ///     search, rules and history completed from a reference share, live candles by polling, the commission rate and the
    ///     account value, and the Invest API transport with its pinned certificate trust and per-token limits. No request leaves the process while <c>Venues:TInvest:Enabled</c> is false.
    /// </summary>
    public static IServiceCollection AddTInvestVenue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IVenueDescriptor, TInvestVenue>();
        services.AddSingleton<IVenueCredentialSchema, TInvestCredentialSchema>();
        services.AddSingleton<IVenueAccountDirectory, TInvestAccountDirectory>();
        services.AddSingleton<ITInvestCallInvokers, TInvestChannelInvokers>();
        services.AddSingleton(provider => new TInvestRequestLimiter(provider.GetRequiredService<IOptions<TInvestOptions>>().Value.Limits, TimeProvider.System));
        services.AddSingleton<TInvestTransport>();
        services.AddSingleton(provider => new TInvestShares(provider.GetRequiredService<TInvestTransport>(), TimeProvider.System));
        services.AddSingleton<TInvestCandleReader>();
        services.AddSingleton<TInvestHistoricalCandleSource>();
        services.AddSingleton<IHistoricalCandleSource>(provider => provider.GetRequiredService<TInvestHistoricalCandleSource>());
        services.AddSingleton<TInvestPrices>();
        services.AddSingleton(provider => new TInvestLiveCandleSource(
            provider.GetRequiredService<TInvestHistoricalCandleSource>(),
            provider.GetRequiredService<IOptions<TInvestOptions>>(),
            TimeProvider.System));
        services.AddSingleton<ILiveCandleSource>(provider => provider.GetRequiredService<TInvestLiveCandleSource>());
        services.AddSingleton<TInvestCandleStreams>();
        services.AddSingleton<IFormingCandleSource>(provider => new TInvestFormingCandleSource(
            provider.GetRequiredService<TInvestShares>(),
            provider.GetRequiredService<TInvestCandleStreams>(),
            TimeProvider.System));
        services.AddSingleton<IVenueFeeRateReader, TInvestFeeRateReader>();
        services.AddSingleton<IAccountBalanceReader>(provider => new TInvestAccountBalanceReader(provider.GetRequiredService<TInvestTransport>(), TimeProvider.System));
        services.AddSingleton<IVenueAccountIdentityReader, TInvestAccountIdentityReader>();
        services.AddSingleton<IInstrumentCatalog, TInvestInstrumentCatalog>();
        services.AddSingleton<IInstrumentRulesSource, TInvestInstrumentRulesSource>();
        services.AddSingleton<IVenueMarketDataAccess, TInvestMarketDataAccess>();
        services.AddSingleton<ITInvestOrderAttempts, TInvestOrderAttemptRepository>();
        services.ConfigureRunner(runner => runner.ScanIn(typeof(TInvestRegistration).Assembly).For.Migrations());
        services.AddOptions<TInvestOptions>()
            .Bind(configuration.GetSection(TInvestOptions.SectionName))
            .Validate(options => options.IsValid(), "Venues:TInvest needs the official HTTPS endpoint, positive limits and a timeout when it is enabled.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    ///     Registers T-Invest order execution for a Worker with the live role. Only called when <c>Live:Trading:Enabled</c> is
    ///     true; every committed setting keeps it false (ADR 0023).
    /// </summary>
    public static IServiceCollection AddTInvestTrading(this IServiceCollection services)
    {
        services.AddSingleton<ITInvestBotContexts, TInvestBotContexts>();
        services.AddSingleton<TInvestOrderReads>();
        services.AddSingleton<TInvestOrderUpdates>();
        services.AddSingleton<TInvestOrderStreams>();
        services.AddSingleton<TInvestTradingStatuses>();
        services.AddSingleton(provider => new TInvestPriceBands(provider.GetRequiredService<TInvestTransport>(), TimeProvider.System));
        services.AddSingleton<ITInvestOrderWaits, TInvestOrderWaits>();
        services.AddSingleton(provider => new TInvestSchedules(
            provider.GetRequiredService<TInvestTransport>(),
            TimeProvider.System,
            provider.GetRequiredService<ILogger<TInvestSchedules>>()));
        services.AddSingleton<TInvestOperationReads>();
        services.AddSingleton<ITInvestBotPositions, TInvestBotPositions>();
        services.AddSingleton<TInvestPositionCoverage>();
        services.AddSingleton(provider => new TInvestOrderProgress(
            provider.GetRequiredService<ITInvestOrderAttempts>(),
            provider.GetRequiredService<ITInvestOrderWaits>(),
            provider.GetRequiredService<TInvestSchedules>(),
            provider.GetRequiredService<TInvestPositionCoverage>(),
            TimeProvider.System));
        services.AddSingleton<TInvestAbsenceProofs>();
        services.AddSingleton<TInvestLateCommissions>();
        services.AddSingleton<TInvestOrderGateway>();
        services.AddSingleton<IOrderGateway>(provider => provider.GetRequiredService<TInvestOrderGateway>());
        services.AddHostedService<TInvestWaitingOrdersWorker>();
        services.AddHostedService<TInvestClosedSessionSweep>();

        return services;
    }
}