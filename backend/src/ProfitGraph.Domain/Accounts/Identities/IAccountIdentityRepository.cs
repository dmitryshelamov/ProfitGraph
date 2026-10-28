using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Accounts.Identities;

public interface IAccountIdentityRepository
{
    Task<AccountIdentity?> Get(ExchangeCredentialId credentialId, CancellationToken cancellationToken);

    Task<AccountIdentity?> GetForUpdate(ExchangeCredentialId credentialId, CancellationToken cancellationToken);

    /// <summary>
    ///     The credentials of the venues with any bot whose account identity is due now: never read, or past their next
    ///     attempt.
    /// </summary>
    Task<IReadOnlyList<DueAccountIdentity>> ListDue(IReadOnlyList<VenueId> venues, DateTimeOffset now, CancellationToken cancellationToken);

    Task Save(AccountIdentity identity, CancellationToken cancellationToken);
}