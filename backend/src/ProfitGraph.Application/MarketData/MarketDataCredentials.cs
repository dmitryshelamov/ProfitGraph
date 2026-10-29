using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     Checks and decrypts the credential a user names for market data reads (ADR 0023, TC6): it must be the user's own and
///     one the series' venue accepts (ADR 0061). Another user's credential is reported as missing; secrets stay in memory for the reads only.
/// </summary>
public sealed class MarketDataCredentials(IExchangeCredentialRepository credentials, ISecretProtector protector, VenueRegistry venues)
{
    public const string VenueMismatch = "credential_venue_mismatch";

    /// <summary>
    ///     Fails unless <paramref name="credentialId" /> is null or the user's credential that <paramref name="venue" /> accepts.
    /// </summary>
    public async Task Check(Guid ownerId, Guid? credentialId, VenueId venue, CancellationToken cancellationToken)
    {
        if (credentialId is { } id)
        {
            Ensure(await credentials.GetOwned(id, ownerId, cancellationToken), venue);
        }
    }

    /// <summary>
    ///     The decrypted credential, or null when none is named.
    /// </summary>
    public async Task<ExchangeCredentialSecretsInternal?> Read(Guid ownerId, Guid? credentialId, VenueId venue, CancellationToken cancellationToken)
    {
        if (credentialId is not { } id)
        {
            return null;
        }

        protector.EnsureAvailable();

        return protector.Decrypt(Ensure(await credentials.GetOwned(id, ownerId, cancellationToken), venue));
    }

    /// <summary>
    ///     For a queued job: like <see cref="Read" />, but a credential deleted since the job was queued gives no access, so a
    ///     run whose history is complete in the cache still runs; a venue that must read without access fails by itself.
    /// </summary>
    public async Task<ExchangeCredentialSecretsInternal?> ReadIfStillThere(Guid ownerId, Guid? credentialId, VenueId venue, CancellationToken cancellationToken)
    {
        try
        {
            return await Read(ownerId, credentialId, venue, cancellationToken);
        }
        catch (ResourceNotFoundException exception) when (exception.Code == ExchangeCredentialErrorCodes.NotFound)
        {
            return null;
        }
    }

    private ExchangeCredential Ensure(ExchangeCredential credential, VenueId venue)
    {
        return venues.AcceptsCredential(venue, credential.Venue)
            ? credential
            : throw new ApplicationValidationException([new InputError(VenueMismatch, "CredentialId", "The credential belongs to another venue.")]);
    }
}