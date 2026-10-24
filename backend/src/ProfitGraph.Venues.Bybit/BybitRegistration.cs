using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Venues.Bybit.Instruments;
using ProfitGraph.Venues.Bybit.LiveData;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit;

public static class BybitRegistration
{
    /// <summary>
    ///     Registers Bybit Spot: its descriptor, public history, live candles, tradable pairs and instrument rules. Every request is public; no credentials are used.
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

        return services;
    }

    private static void Configure(IServiceProvider provider, HttpClient client)
    {
        client.BaseAddress = new Uri(provider.GetRequiredService<BybitOptions>().BaseAddress);
        client.Timeout = Timeout.InfiniteTimeSpan;
    }
}