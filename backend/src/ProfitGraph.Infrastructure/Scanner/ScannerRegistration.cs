using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Scanner.Alerts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.RunScanBacktest;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Infrastructure.Common;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Infrastructure.Scanner.Repositories;

namespace ProfitGraph.Infrastructure.Scanner;

public static class ScannerRegistration
{
    /// <summary>
    ///     The snapshot and preset stores and the scanner's readiness, for every host.
    /// </summary>
    internal static IServiceCollection AddScanner(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ScannerOptions>()
            .Bind(configuration.GetSection(ScannerOptions.SectionName))
            .Validate(options => options.IsValid(), "Scanner needs positive delays, a snapshot delay under 50 s and a snapshot age over a minute.")
            .ValidateOnStart();
        services.AddScoped<IScanSnapshotRepository, ScanSnapshotRepository>();
        services.AddScoped<IScanPresetRepository, ScanPresetRepository>();
        services.AddScoped<IScanAlertWatchRepository, ScanAlertWatchRepository>();
        services.AddScoped<ScanAlertQueue>();
        services.AddScoped<ScanBacktestRepository>();
        services.AddScoped<IScanBacktestRepository>(provider => provider.GetRequiredService<ScanBacktestRepository>());
        services.AddScoped<IScanBacktestReader>(provider => provider.GetRequiredService<ScanBacktestRepository>());
        services.AddBackgroundJob<RunScanBacktestCommand, ScanBacktestCompletedInternal>();
        services.AddSingleton<IScanSnapshotNotifications, ScanSnapshotNotifications>();
        services.AddHealthChecks().AddCheck<ScannerHealthCheck>(ScannerHealthCheck.Name, tags: [HealthTags.Ready]);

        return services;
    }

    /// <summary>
    ///     The Worker's <c>scanner</c> role (ADR 0059).
    /// </summary>
    public static IServiceCollection AddMarketScanner(this IServiceCollection services)
    {
        services.AddSingleton<ScannerLock>();
        services.AddSingleton<IHostedService, ScannerHost>();

        return services;
    }
}