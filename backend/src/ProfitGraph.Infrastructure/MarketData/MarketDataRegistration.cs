using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Application.MarketData.Instruments;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Infrastructure.MarketData.Csv;
using ProfitGraph.Infrastructure.MarketData.Repositories;

namespace ProfitGraph.Infrastructure.MarketData;

internal static class MarketDataRegistration
{
    /// <summary>
    ///     Registers the candle store, CSV import, history downloads and the venue registry; venue modules add their
    ///     descriptors, history sources, instrument catalogs and rules sources.
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
        services.AddOptions<HistoryDownloadOptions>()
            .Bind(configuration.GetSection(HistoryDownloadOptions.SectionName))
            .Validate(options => options.IsValid(), "HistoricalDownload:MaxCandlesPerChunk must be between 1 and 100000.")
            .ValidateOnStart();
        services.AddScoped(provider => provider.GetRequiredService<IOptionsMonitor<HistoryDownloadOptions>>().CurrentValue);
        services.AddScoped(typeof(VenuePorts<>));
        services.AddScoped<CandleDownloadService>();
        services.AddBackgroundJob<DownloadCandlesCommand, HistoricalDownloadInternal>();
        services.AddScoped<IInstrumentRulesRepository, InstrumentRulesRepository>();
        services.AddScoped<InstrumentRulesCaptureService>();
        services.AddScoped<HistoryPreparationService>();

        return services;
    }
}