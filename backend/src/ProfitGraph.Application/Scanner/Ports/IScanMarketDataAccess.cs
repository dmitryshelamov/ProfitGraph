using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Scanner.Ports;

/// <summary>
///     The stored credential a venue without anonymous market data reads its scanner's data with (ADR 0062): the Worker's scan
///     and backtests, and every user's pair chart, levels and trade cards of that venue. A venue with public market data has
///     none.
/// </summary>
public interface IScanMarketDataAccess : IVenuePort
{
    /// <summary>
    ///     The decrypted credential; it fails with <see cref="MarketScanErrorCodes.ProviderUnavailable" /> when the configured
    ///     credential is missing, of another venue or cannot be decrypted.
    /// </summary>
    Task<ExchangeCredentialSecretsInternal> Credential(CancellationToken cancellationToken);
}