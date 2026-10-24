using Bybit.Net;

using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoClients.Net.Interfaces;

using CryptoExchange.Net.Objects;

using Microsoft.Extensions.Logging;

namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     The one setup of the public Bybit REST client: the origin of the HttpClient, waiting on the SDK's own rate limits,
///     no cache and no request coalescing. Retries and the request budget belong to <see cref="BybitResilienceExecutor" />.
/// </summary>
internal static class BybitRestClientFactory
{
    public static IExchangeRestClient Create(HttpClient httpClient, ILoggerFactory loggerFactory, TimeSpan attemptTimeout)
    {
        var configuration = new CryptoClientsConfiguration(builder => builder
            .ConfigureGlobal(options => options.EnabledExchanges = [Exchange.Bybit])
            .ConfigureBybit(options =>
            {
                if (httpClient.BaseAddress is not null)
                {
                    options.Rest.Environment = BybitEnvironment.CreateCustom(
                        "ProfitGraph",
                        httpClient.BaseAddress.AbsoluteUri.TrimEnd('/'),
                        BybitEnvironment.Live.SocketBaseAddress);
                }

                options.Rest.RateLimitingBehaviour = RateLimitingBehaviour.Wait;
                options.Rest.RequestTimeout = attemptTimeout;
                options.Rest.CachingEnabled = false;
                options.Rest.RequestCoalescingEnabled = false;
            }));

        return new ExchangeRestClient(configuration, httpClient, loggerFactory);
    }
}