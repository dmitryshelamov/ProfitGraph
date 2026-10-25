using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Venues.Steam;

public static class SteamRegistration
{
    /// <summary>
    ///     Registers Steam Community Market: its descriptor and backtest defaults. Nothing here contacts Steam; market data,
    ///     sessions and orders arrive with later wave 3 pull requests and stay off while <c>Venues:Steam:Enabled</c> is false.
    /// </summary>
    public static IServiceCollection AddSteamVenue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IVenueDescriptor, SteamVenue>();
        services.AddSingleton<IVenueExecutionDefaults, SteamExecutionDefaults>();
        services.AddOptions<SteamOptions>()
            .Bind(configuration.GetSection(SteamOptions.SectionName))
            .Validate(options => options.IsValid(), "Venues:Steam needs the official HTTPS community origin when it is enabled.")
            .ValidateOnStart();

        return services;
    }
}