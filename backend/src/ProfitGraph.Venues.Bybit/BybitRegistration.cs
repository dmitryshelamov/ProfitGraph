using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Venues.Bybit;

public static class BybitRegistration
{
    /// <summary>
    ///     Registers Bybit Spot as a venue. Public market data ports arrive with the history download.
    /// </summary>
    public static IServiceCollection AddBybitVenue(this IServiceCollection services)
    {
        services.AddSingleton<IVenueDescriptor, BybitVenue>();

        return services;
    }
}