using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The costs of a pair's scanner trades (ADR 0065): the fee rates the trade's bot will pay, read from the venue with the owner's
///     credential as a bot reads them at creation, and the spread of the pair's snapshot row in percent. Without settings, a credential,
///     a fee port or an answer from the venue the cards assume <see cref="ScanTradeOptions.FeeRate" /> on every order; without a spread
///     they take it as zero.
/// </summary>
public sealed class ScanTradeCostsReader(
    IExchangeCredentialRepository credentials,
    ISecretProtector protector,
    VenueRegistry venues,
    VenuePorts<IVenueFeeRateReader> readers,
    ScanTradeFeeRateCache cache,
    ScanTradeOptions options)
{
    private const decimal Percent = 100m;

    public async Task<ScanTradeCostsInternal> Read(
        ScanTradeSettings? settings,
        string venue,
        string symbol,
        decimal? spreadPercent,
        CancellationToken cancellationToken)
    {
        VenueFeeRatesInternal? rates = settings is null ? null : await Rates(settings, venue, symbol, cancellationToken);
        decimal spread = spreadPercent is > 0m ? spreadPercent.Value / Percent : 0m;
        var costs = new ScanTradeCosts(rates?.MakerRate ?? options.FeeRate, rates?.TakerRate ?? options.FeeRate, spread);

        return new ScanTradeCostsInternal(costs, rates is not null, spreadPercent is not null);
    }

    private async Task<VenueFeeRatesInternal?> Rates(ScanTradeSettings settings, string venue, string symbol, CancellationToken cancellationToken)
    {
        Guid credentialId = settings.CredentialId.Value;

        if (cache.Find(credentialId, venue, symbol, options.FeeRatesLifetime) is { } cached)
        {
            return cached;
        }

        CandleSeries series = venues.ResolveSeries(venue, symbol, options.Timeframe);
        ExchangeCredential? credential = await credentials.Get(settings.CredentialId, cancellationToken);

        if (credential is null || credential.OwnerId != settings.OwnerId || !protector.IsAvailable || readers.Find(series.Venue) is not { } reader)
        {
            return null;
        }

        try
        {
            VenueFeeRatesInternal rates = await reader.Read(protector.Decrypt(credential), series.Symbol, cancellationToken);
            cache.Store(credentialId, venue, symbol, rates);

            return rates;
        }
        catch (Exception exception) when (exception is VenueFeeRatesUnavailableException or HttpRequestException)
        {
            return null;
        }
    }
}