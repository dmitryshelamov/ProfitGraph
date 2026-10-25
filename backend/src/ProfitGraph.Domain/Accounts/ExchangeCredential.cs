using System.Collections.Immutable;

using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     A user's secrets for one venue (ADR 0015): named values the venue declares, such as an API key and secret, each
///     stored encrypted (ADR 0022, C6). Only a four-character public hint is visible. A credential belongs to its owner and
///     never changes after creation.
/// </summary>
public sealed class ExchangeCredential : AggregateRoot
{
    private const int MaxFieldLength = 32;
    public ExchangeCredentialId Id { get; }
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public ExchangeCredentialLabel Label { get; }
    public ApiKeyLast4 ApiKeyLast4 { get; }

    /// <summary>
    ///     Every encrypted value by its field name, in ordinal order of the names.
    /// </summary>
    public ImmutableSortedDictionary<string, EncryptedSecret> Secrets { get; }

    public DateTimeOffset CreatedAt { get; }

    private ExchangeCredential(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        IReadOnlyDictionary<string, EncryptedSecret> secrets,
        DateTimeOffset createdAt)
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

        Id = id;
        OwnerId = ownerId;
        Venue = venue;
        Label = label;
        ApiKeyLast4 = apiKeyLast4;
        Secrets = secrets.ToImmutableSortedDictionary(StringComparer.Ordinal);
        CreatedAt = createdAt;
    }

    public static ExchangeCredential Create(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        IReadOnlyDictionary<string, EncryptedSecret> secrets,
        DateTimeOffset now)
    {
        return new ExchangeCredential(id, ownerId, venue, label, apiKeyLast4, secrets, now);
    }

    public static ExchangeCredential Restore(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        IReadOnlyDictionary<string, EncryptedSecret> secrets,
        DateTimeOffset createdAt)
    {
        return new ExchangeCredential(id, ownerId, venue, label, apiKeyLast4, secrets, createdAt);
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    private static bool IsFieldName(string? name)
    {
        return name is { Length: > 0 and <= MaxFieldLength } && name.All(char.IsAsciiLetterOrDigit);
    }
}