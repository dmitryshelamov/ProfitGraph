using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Tests.Integration.MarketData;

public static class TestHistoryRegistration
{
    /// <summary>
    ///     Adds a venue with history served by <see cref="FakeHistorySource" /> and a venue without any history source.
    /// </summary>
    public static IServiceCollection AddTestHistory(this IServiceCollection services)
    {
        services.AddSingleton<IVenueDescriptor>(new TestHistoryVenue(TestHistoryVenue.WithHistory));
        services.AddSingleton<IVenueDescriptor>(new TestHistoryVenue(TestHistoryVenue.WithoutHistory));
        services.AddSingleton<FakeHistorySource>();
        services.AddSingleton<IHistoricalCandleSource>(provider => provider.GetRequiredService<FakeHistorySource>());

        return services;
    }
}