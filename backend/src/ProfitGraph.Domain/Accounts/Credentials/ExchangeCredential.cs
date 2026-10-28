using System.Collections.Immutable;

using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Accounts.Credentials;

/// <summary>
///     A user's secrets for one venue (ADR 0015): named values the venue declares, such as an API key and secret, each
///     stored encrypted (ADR 0022, C6). Only a four-character public hint and a non-secret public identifier are visible. A venue whose credential selects one of
///     several venue accounts records a keyed fingerprint of that account, so one account is stored once (ADR 0023, TC5). A
///     credential belongs to its owner; only its label changes after creation.
/// </summary>
public sealed class ExchangeCredential : AggregateRoot
{
    private const int MaxFieldLength = 32;
    private const int MaxFingerprintLength = 128;
    public ExchangeCredentialId Id { get; }
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public ExchangeCredentialLabel Label { get; private set; }
    public ApiKeyLast4 ApiKeyLast4 { get; }

    /// <summary>
    ///     Every encrypted value by its field name, in ordinal order of the names.
    /// </summary>
    public ImmutableSortedDictionary<string, EncryptedSecret> Secrets { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    ///     The keyed fingerprint of the venue account the credential selects, or null for a venue without account selection.
    /// </summary>
    public string? AccountFingerprint { get; }

    /// <summary>
    ///     The non-secret identifier shown beside the masked key, or null for a credential stored before it was recorded.
    /// </summary>
    public CredentialPublicId? PublicId { get; }

    private ExchangeCredential(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        IReadOnlyDictionary<string, EncryptedSecret> secrets,
        DateTimeOffset createdAt,
        string? accountFingerprint,
        CredentialPublicId? publicId)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(apiKeyLast4);
        ArgumentNullException.ThrowIfNull(secrets);

        if (secrets.Count == 0 || secrets.Any(secret => !IsFieldName(secret.Key) || secret.Value is null))
        {
            throw new ArgumentException($"A credential needs encrypted values named by 1 to {MaxFieldLength} ASCII letters or digits.", nameof(secrets));
        }

        if (venue.Value is null)
        {
            throw new ArgumentException("A credential needs a venue.", nameof(venue));
        }

        if (accountFingerprint is not null && (accountFingerprint.Length is 0 or > MaxFingerprintLength || accountFingerprint.Any(char.IsWhiteSpace)))
        {
            throw new ArgumentException($"An account fingerprint has 1 to {MaxFingerprintLength} characters without spaces.", nameof(accountFingerprint));
        }

        Id = id;
        OwnerId = ownerId;
        Venue = venue;
        Label = label;
        ApiKeyLast4 = apiKeyLast4;
        Secrets = secrets.ToImmutableSortedDictionary(StringComparer.Ordinal);
        CreatedAt = createdAt;
        AccountFingerprint = accountFingerprint;
        PublicId = publicId;
    }

    public static ExchangeCredential Create(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        IReadOnlyDictionary<string, EncryptedSecret> secrets,
        DateTimeOffset now,
        string? accountFingerprint = null,
        CredentialPublicId? publicId = null)
    {
        return new ExchangeCredential(id, ownerId, venue, label, apiKeyLast4, secrets, now, accountFingerprint, publicId);
    }

    public static ExchangeCredential Restore(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        IReadOnlyDictionary<string, EncryptedSecret> secrets,
        DateTimeOffset createdAt,
        string? accountFingerprint = null,
        CredentialPublicId? publicId = null)
    {
        return new ExchangeCredential(id, ownerId, venue, label, apiKeyLast4, secrets, createdAt, accountFingerprint, publicId);
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    /// <summary>
    ///     Gives the credential a new name; its secrets and the venue account it selects stay as they are. Returns whether the
    ///     label changed.
    /// </summary>
    public bool Rename(ExchangeCredentialLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (label == Label)
        {
            return false;
        }

        Label = label;

        return true;
    }

    private static bool IsFieldName(string? name)
    {
        return name is { Length: > 0 and <= MaxFieldLength } && name.All(char.IsAsciiLetterOrDigit);
    }
}