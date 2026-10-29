using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Signed Bybit order writes: creating and cancelling orders and, for USDT perpetuals, setting a leverage (ADR 0061). Only a Worker with the live role and trading enabled
///     registers it (ADR 0017). Factory logging is removed so no header reaches a log.
/// </summary>
internal sealed class BybitTradeClient(HttpClient http, BybitServerClock serverClock)
{
    // A USDT perpetual's entry sets the bot's leverage first (ADR 0061).
    private static readonly string[] Paths = ["/v5/order/create", "/v5/order/cancel", "/v5/position/set-leverage"];

    public Task<BybitRestResult> Post(
        ExchangeCredentialSecretsInternal credential,
        string path,
        string body,
        Func<CancellationToken, Task>? beforeSend,
        CancellationToken cancellationToken)
    {
        if (!Paths.Contains(path, StringComparer.Ordinal))
        {
            throw new ArgumentException("The path is not a Bybit order write.", nameof(path));
        }

        return BybitRestTransport.Signed(http, serverClock, HttpMethod.Post, path, body, body, credential, beforeSend, cancellationToken);
    }
}