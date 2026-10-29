using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Venues.Bybit.Instruments;
using ProfitGraph.Venues.Bybit.LiveData;
using ProfitGraph.Venues.Bybit.MarketData;
using ProfitGraph.Venues.Bybit.Scanner;
using ProfitGraph.Venues.Bybit.Simulation;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Venues.Bybit;

public static class BybitRegistration
{
    private const string ScanKlineClient = "bybit-scan-klines";
    private const string ScanQuoteClient = "bybit-scan-quotes";
    private const string LinearKey = "bybit-linear";
    private const string LinearHistoryClient = "bybit-linear-history";
    private const string LinearInstrumentsClient = "bybit-linear-instruments";
    private const string LinearCatalogClient = "bybit-linear-catalog";
    private const string LinearScanKlineClient = "bybit-linear-scan-klines";
    private const string LinearScanQuoteClient = "bybit-linear-scan-quotes";

    /// <summary>
    ///     Registers Bybit Spot and Bybit's USDT perpetuals (ADR 0061): their descriptors, public history, live candles, tradable pairs,
    ///     instrument rules, the market scanner's data and the read-only fee rates of a credential. No order can be sent.
    /// </summary>
    public static IServiceCollection AddBybitVenue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IVenueDescriptor, BybitVenue>();
        services.AddOptions<BybitOptions>()
            .Bind(configuration.GetSection(BybitOptions.SectionName))
            .Validate(options => options.IsValid(), "Venues:Bybit needs an HTTP(S) origin and consistent retry and circuit settings.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<BybitOptions>>().Value);
        services.AddSingleton(provider => new BybitResilienceExecutor(
            TimeProvider.System,
            provider.GetRequiredService<BybitOptions>(),
            provider.GetRequiredService<ILogger<BybitResilienceExecutor>>()));
        services.AddTransient<BybitResponseGuardHandler>();
        services.AddHttpClient<IHistoricalCandleSource, BybitHistoricalCandleSource>(Configure)
            .AddHttpMessageHandler<BybitResponseGuardHandler>();
        services.AddSingleton(provider => new BybitInstrumentList(TimeProvider.System, provider.GetRequiredService<BybitOptions>().InstrumentListLifetime));
        services.AddTransient<BybitInstrumentsResponseGuardHandler>();
        services.AddHttpClient<IInstrumentRulesSource, BybitInstrumentRulesSource>(Configure)
            .AddHttpMessageHandler<BybitInstrumentsResponseGuardHandler>();

        // The list is only for picking a pair; its rules are read again, guarded, when they are captured.
        services.AddHttpClient<IInstrumentCatalog, BybitInstrumentCatalog>(Configure);
        services.AddSingleton<IBybitPublicStreamTransport, BybitPublicWebSocketTransport>();
        services.AddSingleton<ILiveCandleSource, BybitLiveCandleSource>();
        services.AddSingleton<ILiveMarkPriceSource, BybitLiveMarkPriceSource>();
        services.AddSingleton<IFormingCandleSource, BybitFormingCandleSource>();
        services.AddSingleton(provider => new BybitServerClock(provider.GetRequiredService<BybitOptions>(), TimeProvider.System));
        services.AddHttpClient<BybitReadClient>(Configure).RemoveAllLoggers();
        services.AddTransient<IVenueFeeRateReader, BybitFeeRateReader>();
        services.AddTransient<IVenueRestingOrderCapacity, BybitRestingOrderCapacity>();
        services.AddTransient<IAccountBalanceReader, BybitAccountBalanceReader>();
        services.AddTransient<IVenueAccountIdentityReader, BybitAccountIdentityReader>();
        services.AddSingleton<IVenueCredentialSchema, BybitCredentialSchema>();
        services.AddSingleton(provider => new BybitScanRequestBudget(provider.GetRequiredService<BybitOptions>(), TimeProvider.System));
        services.AddHttpClient(ScanKlineClient, Configure).AddHttpMessageHandler<BybitResponseGuardHandler>();
        services.AddHttpClient(ScanQuoteClient, Configure);
        services.AddTransient<IMarketScanSource>(provider =>
        {
            var clients = provider.GetRequiredService<IHttpClientFactory>();

            return ActivatorUtilities.CreateInstance<BybitMarketScanSource>(
                provider,
                clients.CreateClient(ScanKlineClient),
                clients.CreateClient(ScanQuoteClient));
        });

        return services.AddBybitLinearVenue();
    }

    /// <summary>
    ///     Registers Bybit Spot order execution. Only a Worker with the live role calls it, and only when
    ///     <c>Live:Trading:Enabled</c> is true; every committed setting keeps it false (ADR 0017).
    /// </summary>
    public static IServiceCollection AddBybitTrading(this IServiceCollection services)
    {
        services.AddHttpClient<BybitTradeClient>(Configure).RemoveAllLoggers();
        services.AddSingleton<BybitOrderUpdates>();
        services.AddSingleton<BybitBotContexts>();
        services.AddSingleton<BybitOrderReads>();
        services.AddSingleton(provider => new BybitAbsenceProofs(
            provider.GetRequiredService<BybitOrderReads>(),
            provider.GetRequiredService<BybitOptions>(),
            TimeProvider.System));
        services.AddSingleton<IBybitPrivateStreamTransport, BybitPrivateWebSocketTransport>();
        services.TryAddSingleton(new PrivateStreamReconnectPolicy());
        services.AddSingleton(provider => ActivatorUtilities.CreateInstance<BybitPrivateStreams>(provider, TimeProvider.System));
        services.AddTransient<IOrderGateway, BybitOrderGateway>();
        services.AddTransient<IOrderGateway>(provider => ActivatorUtilities.CreateInstance<BybitOrderGateway>(provider, BybitMarket.Linear));

        return services;
    }

    /// <summary>
    ///     Registers Bybit order execution simulated in memory (<see cref="BybitSimulationOptions" />) for end-to-end tests:
    ///     bots trade without any request leaving the process. Never together with <see cref="AddBybitTrading" />.
    /// </summary>
    public static IServiceCollection AddBybitSimulatedTrading(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BybitSimulationOptions>()
            .Bind(configuration.GetSection(BybitSimulationOptions.SectionName))
            .Validate(options => options.IsValid(), "Live:Simulation needs a positive market price and a fee rate below 10%.")
            .ValidateOnStart();
        services.AddSingleton<IOrderGateway>(provider => new BybitSimulatedOrderGateway(provider.GetRequiredService<IOptions<BybitSimulationOptions>>().Value));
        services.AddSingleton<IOrderGateway>(provider => new BybitSimulatedOrderGateway(
            provider.GetRequiredService<IOptions<BybitSimulationOptions>>().Value,
            BybitMarket.Linear));

        return services;
    }

    /// <summary>
    ///     The USDT-perpetual venue <c>bybit-linear</c> (ADR 0061): the Spot classes for the linear category, its own instrument list,
    ///     public stream and scanner data, and the bot settings that carry the leverage. Its bots use the Spot credential.
    /// </summary>
    private static IServiceCollection AddBybitLinearVenue(this IServiceCollection services)
    {
        services.AddSingleton<IVenueDescriptor, BybitLinearVenue>();
        services.AddHttpClient(LinearHistoryClient, Configure).AddHttpMessageHandler<BybitResponseGuardHandler>();
        services.AddTransient<IHistoricalCandleSource>(provider =>
            ActivatorUtilities.CreateInstance<BybitHistoricalCandleSource>(provider, Client(provider, LinearHistoryClient), BybitMarket.Linear));
        services.AddHttpClient(LinearInstrumentsClient, Configure).AddHttpMessageHandler(() => new BybitInstrumentsResponseGuardHandler(BybitMarket.Linear));
        services.AddTransient<IInstrumentRulesSource>(provider =>
            ActivatorUtilities.CreateInstance<BybitLinearInstrumentRulesSource>(provider, Client(provider, LinearInstrumentsClient)));
        services.AddKeyedSingleton(
            LinearKey,
            (provider, _) => new BybitInstrumentList(TimeProvider.System, provider.GetRequiredService<BybitOptions>().InstrumentListLifetime));
        services.AddHttpClient(LinearCatalogClient, Configure);
        services.AddTransient<IInstrumentCatalog>(provider => ActivatorUtilities.CreateInstance<BybitLinearInstrumentCatalog>(
            provider,
            Client(provider, LinearCatalogClient),
            provider.GetRequiredKeyedService<BybitInstrumentList>(LinearKey)));
        services.AddKeyedSingleton<IBybitPublicStreamTransport>(
            LinearKey,
            (provider, _) => new BybitPublicWebSocketTransport(provider.GetRequiredService<BybitOptions>(), BybitMarket.Linear));
        services.AddSingleton<ILiveCandleSource>(provider =>
            ActivatorUtilities.CreateInstance<BybitLiveCandleSource>(provider, LinearTransport(provider), BybitMarket.Linear));
        services.AddSingleton<ILiveMarkPriceSource>(provider =>
            ActivatorUtilities.CreateInstance<BybitLiveMarkPriceSource>(provider, LinearTransport(provider), BybitMarket.Linear));
        services.AddSingleton<IFormingCandleSource>(provider =>
            ActivatorUtilities.CreateInstance<BybitFormingCandleSource>(provider, LinearTransport(provider), BybitMarket.Linear));
        services.AddTransient<IVenueFeeRateReader>(provider => ActivatorUtilities.CreateInstance<BybitFeeRateReader>(provider, BybitMarket.Linear));
        services.AddTransient<IVenueRestingOrderCapacity>(provider => ActivatorUtilities.CreateInstance<BybitRestingOrderCapacity>(provider, BybitMarket.Linear));

        // No credential schema: futures take Spot's key and no key is enrolled for them. The account readers keep the cards of
        // credentials enrolled for bybit-linear before one key served both.
        services.AddTransient<IAccountBalanceReader>(provider => ActivatorUtilities.CreateInstance<BybitAccountBalanceReader>(provider, BybitMarket.Linear));
        services.AddTransient<IVenueAccountIdentityReader>(provider => ActivatorUtilities.CreateInstance<BybitAccountIdentityReader>(provider, BybitMarket.Linear));
        services.AddSingleton<BybitLinearBotSettingsSchema>();
        services.AddSingleton<IVenueBotSettings>(provider => provider.GetRequiredService<BybitLinearBotSettingsSchema>());
        services.AddSingleton<IVenueLeverage>(provider => provider.GetRequiredService<BybitLinearBotSettingsSchema>());
        services.AddHttpClient(LinearScanKlineClient, Configure).AddHttpMessageHandler<BybitResponseGuardHandler>();
        services.AddHttpClient(LinearScanQuoteClient, Configure);
        services.AddTransient<IMarketScanSource>(provider => ActivatorUtilities.CreateInstance<BybitMarketScanSource>(
            provider,
            Client(provider, LinearScanKlineClient),
            Client(provider, LinearScanQuoteClient),
            LinearTransport(provider),
            BybitMarket.Linear));

        return services;
    }

    private static HttpClient Client(IServiceProvider provider, string name)
    {
        return provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);
    }

    private static IBybitPublicStreamTransport LinearTransport(IServiceProvider provider)
    {
        return provider.GetRequiredKeyedService<IBybitPublicStreamTransport>(LinearKey);
    }

    private static void Configure(IServiceProvider provider, HttpClient client)
    {
        client.BaseAddress = new Uri(provider.GetRequiredService<BybitOptions>().BaseAddress);
        client.Timeout = Timeout.InfiniteTimeSpan;
    }
}