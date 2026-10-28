using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     A venue's read-only description of the account a credential trades on and of the key itself (ADR 0049): the account's
///     identifier and name, its kind, the key's access level, permissions and expiry. The Worker calls it at most once a day per
///     credential and caches the answer; no page view reaches it. It reports any failure as a reason instead of throwing, and
///     never returns a key, token or secret. A venue without this port shows only what its credential stores.
/// </summary>
public interface IVenueAccountIdentityReader : IVenuePort
{
    Task<AccountIdentityReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken);
}