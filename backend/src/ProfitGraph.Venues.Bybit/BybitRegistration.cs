using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Venues.Bybit.Instruments;
using ProfitGraph.Venues.Bybit.LiveData;
using ProfitGraph.Venues.Bybit.MarketData;
using ProfitGraph.Venues.Bybit.Simulation;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Venues.Bybit;

public static class BybitRegistration
{
    /// <summary>
    ///     Registers Bybit Spot: its descriptor, public history, live candles, tradable pairs, instrument rules and the read-only fee rates of a credential. No order can be sent.
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
        services.AddSingleton(_ => new BybitServerClock(TimeProvider.System));
        services.AddHttpClient<BybitReadClient>(Configure).RemoveAllLoggers();
        services.AddTransient<IVenueFeeRateReader, BybitFeeRateReader>();
        services.AddTransient<IAccountBalanceReader, BybitAccountBalanceReader>();

        return services;
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
        services.AddSingleton<IBybitPrivateStreamTransport, BybitPrivateWebSocketTransport>();
        services.AddSingleton<BybitPrivateStreams>();
        services.AddTransient<IOrderGateway, BybitOrderGateway>();

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

        return services;
    }

    private static void Configure(IServiceProvider provider, HttpClient client)
    {
        client.BaseAddress = new Uri(provider.GetRequiredService<BybitOptions>().BaseAddress);
        client.Timeout = Timeout.InfiniteTimeSpan;
    }
}