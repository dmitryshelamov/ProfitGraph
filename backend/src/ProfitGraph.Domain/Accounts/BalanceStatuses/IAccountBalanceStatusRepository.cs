using ProfitGraph.Domain.Accounts.Credentials;

namespace ProfitGraph.Domain.Accounts.BalanceStatuses;

public interface IAccountBalanceStatusRepository
{
    Task<AccountBalanceStatus?> Get(ExchangeCredentialId credentialId, CancellationToken cancellationToken);

    Task<AccountBalanceStatus?> GetForUpdate(ExchangeCredentialId credentialId, CancellationToken cancellationToken);

    /// <summary>
    ///     The credentials of any bot whose balance is due now: never read, or past their next attempt.
    /// </summary>
    Task<IReadOnlyList<DueAccountBalance>> ListDue(DateTimeOffset now, CancellationToken cancellationToken);

    Task Save(AccountBalanceStatus status, CancellationToken cancellationToken);
}