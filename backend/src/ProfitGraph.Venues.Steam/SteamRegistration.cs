using System.Net;

using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Persistence.Repositories;
using ProfitGraph.Venues.Steam.Session;

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
        services.AddHttpClient<SteamHttpTransport>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All
            })
            .RemoveAllLoggers();
        services.AddSingleton<SteamRequestScheduler>();
        services.AddSingleton<SteamAccountSessions>();
        services.AddOptions<SteamOptions>()
            .Bind(configuration.GetSection(SteamOptions.SectionName))
            .Validate(options => options.IsValid(), "Venues:Steam needs the official HTTPS origins of Community, login and Web API when it is enabled.")
            .ValidateOnStart();

        return services;
    }
}