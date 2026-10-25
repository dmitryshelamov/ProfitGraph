using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Accounts;

public interface IExchangeCredentialRepository
{
    Task<ExchangeCredential?> Get(ExchangeCredentialId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The owner's credentials, newest first.
    /// </summary>
    Task<IReadOnlyList<ExchangeCredential>> List(UserId ownerId, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores a new credential; returns false, storing nothing, when a credential of the venue already has its account
    ///     fingerprint.
    /// </summary>
    Task<bool> Add(ExchangeCredential credential, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether a credential of the venue records any of the fingerprints (one per configured master key).
    /// </summary>
    Task<bool> AnyAccountFingerprint(VenueId venue, IReadOnlyList<string> fingerprints, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes the owner's credential; returns false when the owner has no credential with that identifier.
    /// </summary>
    Task<bool> Delete(ExchangeCredentialId id, UserId ownerId, CancellationToken cancellationToken);
}