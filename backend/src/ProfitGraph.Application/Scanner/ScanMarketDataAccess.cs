using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The scanner's credential of a venue as market data access for the scanner's history reads through the shared candle cache
///     (ADR 0062), or null for a venue with public market data. The caller opens it with <see cref="MarketDataAccess.Use" /> in its
///     own method.
/// </summary>
public sealed class ScanMarketDataAccess(VenuePorts<IScanMarketDataAccess> venues)
{
    public async Task<ExchangeCredentialSecretsInternal?> Read(string venue, CancellationToken cancellationToken)
    {
        return venues.Find(new VenueId(venue)) is { } access ? await access.Credential(cancellationToken) : null;
    }
}