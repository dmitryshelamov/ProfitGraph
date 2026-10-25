using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     The caller's credential that venue reads of market data use, for a venue without anonymous market data
///     (ADR 0023, TC6). A use case opens it around its history, search and rules reads; it flows through the awaited calls
///     of that use case only and never into a run's configuration, fingerprint or history identity. Venues with public
///     market data ignore it.
/// </summary>
public static class MarketDataAccess
{
    private static readonly AsyncLocal<ExchangeCredentialSecretsInternal?> Ambient = new AsyncLocal<ExchangeCredentialSecretsInternal?>();

    public static ExchangeCredentialSecretsInternal? Current => Ambient.Value;

    /// <summary>
    ///     Makes <paramref name="credential" /> the access until the result is disposed; null opens none.
    /// </summary>
    public static IDisposable Use(ExchangeCredentialSecretsInternal? credential)
    {
        var scope = new MarketDataAccessScope(Ambient.Value, previous => Ambient.Value = previous);
        Ambient.Value = credential;

        return scope;
    }
}