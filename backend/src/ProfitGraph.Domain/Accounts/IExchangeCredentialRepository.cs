namespace ProfitGraph.Domain.Accounts;

public interface IExchangeCredentialRepository
{
    Task<ExchangeCredential?> Get(ExchangeCredentialId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The owner's credentials, newest first.
    /// </summary>
    Task<IReadOnlyList<ExchangeCredential>> List(UserId ownerId, CancellationToken cancellationToken);

    Task Add(ExchangeCredential credential, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes the owner's credential; returns false when the owner has no credential with that identifier.
    /// </summary>
    Task<bool> Delete(ExchangeCredentialId id, UserId ownerId, CancellationToken cancellationToken);
}