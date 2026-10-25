using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Tests.Integration.Bots;

namespace ProfitGraph.Tests.Integration.MarketData;

public static class TestHistoryRegistration
{
    /// <summary>
    ///     Adds a venue with history and instruments served by <see cref="FakeHistorySource" /> and <see cref="FakeInstrumentSource" />
    ///     with fee rates from <see cref="FakeFeeRateReader" />,
    ///     a venue without any of them, and a venue whose credential selects an account of <see cref="FakeAccountDirectory" />.
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
        services.AddSingleton<IVenueExecutionDefaults>(new TestExecutionDefaults());
        services.AddSingleton<IVenueCredentialSchema>(new TestCredentialSchema(TestHistoryVenue.WithHistory));
        services.AddSingleton<IVenueCredentialSchema>(new TestCredentialSchema(TestHistoryVenue.WithoutHistory));
        services.AddSingleton<FakeFeeRateReader>();
        services.AddSingleton<IVenueFeeRateReader>(provider => provider.GetRequiredService<FakeFeeRateReader>());
        services.AddSingleton<IVenueBotSettings, FakeVenueBotSettings>();
        services.AddSingleton<IVenueDescriptor>(new TestHistoryVenue(TestHistoryVenue.WithAccounts));
        services.AddSingleton<IVenueCredentialSchema>(new TestAccountCredentialSchema());
        services.AddSingleton<FakeAccountDirectory>();
        services.AddSingleton<IVenueAccountDirectory>(provider => provider.GetRequiredService<FakeAccountDirectory>());

        return services;
    }
}