using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     The credential's current maker and taker rates of the market (Spot, or USDT perpetuals by ADR 0061) of one symbol (<c>GET /v5/account/fee-rate</c>, read only).
/// </summary>
internal sealed class BybitFeeRateReader(BybitReadClient client, BybitMarket? market = null) : IVenueFeeRateReader
{
    private readonly BybitMarket _market = market ?? BybitMarket.Spot;

    public VenueId Venue => _market.Venue;

    public async Task<VenueFeeRatesInternal> Read(ExchangeCredentialSecretsInternal credential, InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        BybitRestResult result = await client.Get(
            credential,
            "/v5/account/fee-rate",
            $"category={_market.Name}&symbol={Uri.EscapeDataString(symbol.Value)}",
            cancellationToken);

        try
        {
            JsonElement rates = result.Body.GetProperty("result").GetProperty("list")[0];

            return result.Outcome == BybitRestOutcome.Accepted
                ? new VenueFeeRatesInternal(Rate(rates, "makerFeeRate"), Rate(rates, "takerFeeRate"))
                : throw new VenueFeeRatesUnavailableException();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new VenueFeeRatesUnavailableException();
        }
    }

    private static decimal Rate(JsonElement rates, string name)
    {
        return decimal.Parse(rates.GetProperty(name).GetString()!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}