using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     A venue's read of an exchange account's value with a credential (Profit Fox Designer ADR 0069). It reads balances
///     only, never orders, and reports any failure as a reason instead of throwing.
/// </summary>
public interface IAccountBalanceReader : IVenuePort
{
    Task<AccountBalanceReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken);
}