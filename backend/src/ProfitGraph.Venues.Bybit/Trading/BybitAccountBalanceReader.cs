using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Reads a Bybit UNIFIED account's value with a signed <c>GET /v5/account/wallet-balance</c> (Profit Fox Designer
///     ADR 0069): account equity in USD, the available balance when Bybit reports one, and every coin. Equity is required;
///     a malformed coin is skipped without losing the totals. Failures carry only a reason, an HTTP status or a Bybit code.
/// </summary>
internal sealed class BybitAccountBalanceReader(BybitReadClient read, TimeProvider clock, BybitMarket? market = null) : IAccountBalanceReader
{
    private readonly BybitMarket _market = market ?? BybitMarket.Spot;

    public VenueId Venue => _market.Venue;

    public async Task<AccountBalanceReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken)
    {
        BybitRestResult result = await read.Get(credential, "/v5/account/wallet-balance", "accountType=UNIFIED", cancellationToken);

        if (result.Outcome != BybitRestOutcome.Accepted)
        {
            return AccountBalanceReadInternal.Failed(
                result.Outcome == BybitRestOutcome.Rejected ? "venue_rejected" : "venue_unavailable",
                result.Code is { } code ? code.ToString(CultureInfo.InvariantCulture) : null);
        }

        try
        {
            JsonElement account = result.Body.GetProperty("result").GetProperty("list")[0];

            if (Decimal(account, "totalEquity") is not { } equity)
            {
                return AccountBalanceReadInternal.Failed("unsupported_response", "totalEquity");
            }

            List<AccountCoinBalanceInternal> coins = [];
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (account.TryGetProperty("coin", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement coin in list.EnumerateArray())
                {
                    if (coin.TryGetProperty("coin", out JsonElement name) && name.GetString() is { Length: > 0 } symbol && seen.Add(symbol))
                    {
                        coins.Add(new AccountCoinBalanceInternal(symbol, Decimal(coin, "walletBalance") ?? 0m, Decimal(coin, "usdValue")));
                    }
                }
            }

            return AccountBalanceReadInternal.Observed(
                new AccountBalanceObservationInternal(equity, "USD", Decimal(account, "totalAvailableBalance"), coins, clock.GetUtcNow()));
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return AccountBalanceReadInternal.Failed("unsupported_response", "list");
        }
    }

    /// <summary>
    ///     A decimal string, also in exponent notation; empty or missing is unknown.
    /// </summary>
    private static decimal? Decimal(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
                ? number
                : null;
    }
}