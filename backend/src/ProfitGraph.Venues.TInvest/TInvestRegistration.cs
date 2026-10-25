using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest;

public static class TInvestRegistration
{
    /// <summary>
    ///     Registers T-Invest MOEX shares (ADR 0023): the descriptor and the Invest API transport with its pinned certificate
    ///     trust and per-token limits. No request leaves the process while <c>Venues:TInvest:Enabled</c> is false.
    /// </summary>
    public static IServiceCollection AddTInvestVenue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IVenueDescriptor, TInvestVenue>();
        services.AddSingleton<ITInvestCallInvokers, TInvestChannelInvokers>();
        services.AddSingleton(provider => new TInvestRequestLimiter(provider.GetRequiredService<IOptions<TInvestOptions>>().Value.Limits, TimeProvider.System));
        services.AddSingleton<TInvestTransport>();
        services.AddOptions<TInvestOptions>()
            .Bind(configuration.GetSection(TInvestOptions.SectionName))
            .Validate(options => options.IsValid(), "Venues:TInvest needs the official HTTPS endpoint, positive limits and a timeout when it is enabled.")
            .ValidateOnStart();

        return services;
    }
}