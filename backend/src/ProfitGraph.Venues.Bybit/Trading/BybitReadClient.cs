using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Signed Bybit reads only: GET requests to an exact allowlist, plus public market data. It has no way to place,
///     amend or cancel an order, so every process may have it. Factory logging is removed so no header reaches a log.
/// </summary>
internal sealed class BybitReadClient(HttpClient http, BybitServerClock serverClock)
{
    private static readonly string[] SignedPaths = ["/v5/account/fee-rate", "/v5/order/realtime", "/v5/order/history", "/v5/execution/list", "/v5/user/query-api"];

    public Task<BybitRestResult> Get(ExchangeCredentialSecretsInternal credential, string path, string query, CancellationToken cancellationToken)
    {
        if (!SignedPaths.Contains(path, StringComparer.Ordinal))
        {
            throw new ArgumentException("The path is not an allowed Bybit read.", nameof(path));
        }

        return BybitRestTransport.Signed(
            http,
            serverClock,
            HttpMethod.Get,
            query.Length == 0 ? path : $"{path}?{query}",
            query,
            body: null,
            credential,
            beforeSend: null,
            cancellationToken);
    }

    /// <summary>
    ///     Bybit's server time in milliseconds, or null when it cannot be measured now.
    /// </summary>
    public Task<long?> ServerTime(CancellationToken cancellationToken)
    {
        return serverClock.Now(token => BybitRestTransport.Public(http, "/v5/market/time", token), cancellationToken);
    }

    public Task<BybitRestResult> Public(string pathAndQuery, CancellationToken cancellationToken)
    {
        if (!pathAndQuery.StartsWith("/v5/market/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only public market data is read without a credential.", nameof(pathAndQuery));
        }

        return BybitRestTransport.Public(http, pathAndQuery, cancellationToken);
    }
}