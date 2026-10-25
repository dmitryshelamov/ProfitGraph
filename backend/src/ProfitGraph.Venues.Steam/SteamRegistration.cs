using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Persistence.Repositories;

namespace ProfitGraph.Venues.Steam;

public static class SteamRegistration
{
    /// <summary>
    ///     Registers Steam Community Market: its descriptor, backtest defaults, credential fields, its own tables and the
    ///     console import of an existing Steam Guard. Nothing here contacts Steam; market data, sessions and orders arrive
    ///     with later wave 3 pull requests and stay off while <c>Venues:Steam:Enabled</c> is false.
    /// </summary>
    public static IServiceCollection AddSteamVenue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IVenueDescriptor, SteamVenue>();
        services.AddSingleton<IVenueExecutionDefaults, SteamExecutionDefaults>();
        services.AddSingleton<IVenueCredentialSchema, SteamCredentialSchema>();
        services.AddScoped<SteamAccountRepository>();
        services.AddScoped<IOperatorCommand, SteamImportCommand>();
        services.ConfigureRunner(runner => runner.ScanIn(typeof(SteamRegistration).Assembly).For.Migrations());
        services.AddOptions<SteamOptions>()
            .Bind(configuration.GetSection(SteamOptions.SectionName))
            .Validate(options => options.IsValid(), "Venues:Steam needs the official HTTPS community origin when it is enabled.")
            .ValidateOnStart();

        return services;
    }
}