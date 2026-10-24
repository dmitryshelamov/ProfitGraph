using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Tests.Integration.MarketData;

public static class TestHistoryRegistration
{
    /// <summary>
    ///     Adds a venue with history and instruments served by <see cref="FakeHistorySource" /> and <see cref="FakeInstrumentSource" />,
    ///     and a venue without any of them.
    /// </summary>
    public static IServiceCollection AddTestHistory(this IServiceCollection services)
    {
        services.AddSingleton<IVenueDescriptor>(new TestHistoryVenue(TestHistoryVenue.WithHistory));
        services.AddSingleton<IVenueDescriptor>(new TestHistoryVenue(TestHistoryVenue.WithoutHistory));
        services.AddSingleton<FakeHistorySource>();
        services.AddSingleton<IHistoricalCandleSource>(provider => provider.GetRequiredService<FakeHistorySource>());
        services.AddSingleton<FakeInstrumentSource>();
        services.AddSingleton<IInstrumentRulesSource>(provider => provider.GetRequiredService<FakeInstrumentSource>());
        services.AddSingleton<IInstrumentCatalog>(provider => provider.GetRequiredService<FakeInstrumentSource>());

        return services;
    }
}