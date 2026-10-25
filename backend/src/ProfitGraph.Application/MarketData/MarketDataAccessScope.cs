using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     Restores the market data access that was current before <see cref="MarketDataAccess.Use" />.
/// </summary>
internal sealed class MarketDataAccessScope(ExchangeCredentialSecretsInternal? previous, Action<ExchangeCredentialSecretsInternal?> restore) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            restore(previous);
        }
    }
}