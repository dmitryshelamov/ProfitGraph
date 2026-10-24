using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Infrastructure.MarketData.Csv;
using ProfitGraph.Infrastructure.MarketData.Repositories;

namespace ProfitGraph.Infrastructure.MarketData;

internal static class MarketDataRegistration
{
    /// <summary>
    ///     Registers the candle store, CSV import and the venue registry; venue modules add their descriptors.
    /// </summary>
    internal static IServiceCollection AddMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CsvImportOptions>()
            .Bind(configuration.GetSection(CsvImportOptions.SectionName))
            .Validate(options => options.IsValid(), "CsvImport limits are inconsistent.")
            .ValidateOnStart();

        // Each request takes one snapshot of the limits, so a configuration reload never changes an import in progress.
        services.AddScoped(provider => provider.GetRequiredService<IOptionsMonitor<CsvImportOptions>>().CurrentValue);
        services.AddSingleton<VenueRegistry>();
        services.AddScoped<ICsvCandleReader, CsvCandleReader>();
        services.AddScoped<ICandleRepository, CandleRepository>();

        return services;
    }
}