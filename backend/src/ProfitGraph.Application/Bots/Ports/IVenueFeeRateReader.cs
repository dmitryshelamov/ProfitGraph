using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A venue's current maker and taker fee rates of one instrument for one credential, read with that credential.
///     A venue without this port cannot run bots yet. Failures are <see cref="VenueFeeRatesUnavailableException" />.
/// </summary>
public interface IVenueFeeRateReader : IVenuePort
{
    Task<VenueFeeRatesInternal> Read(ExchangeCredentialSecretsInternal credential, InstrumentSymbol symbol, CancellationToken cancellationToken);
}