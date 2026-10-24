using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     An exchange API key and secret of one user for one venue (ADR 0015). Both values are stored encrypted; only the
///     final four characters of the key are visible. A credential belongs to its owner and never changes after creation.
/// </summary>
public sealed class ExchangeCredential : AggregateRoot
{
    public ExchangeCredentialId Id { get; }
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public ExchangeCredentialLabel Label { get; }
    public ApiKeyLast4 ApiKeyLast4 { get; }
    public EncryptedSecret ApiKey { get; }
    public EncryptedSecret ApiSecret { get; }
    public DateTimeOffset CreatedAt { get; }

    private ExchangeCredential(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        EncryptedSecret apiKey,
        EncryptedSecret apiSecret,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(apiKeyLast4);
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(apiSecret);

        if (venue.Value is null)
        {
            throw new ArgumentException("A credential needs a venue.", nameof(venue));
        }

        Id = id;
        OwnerId = ownerId;
        Venue = venue;
        Label = label;
        ApiKeyLast4 = apiKeyLast4;
        ApiKey = apiKey;
        ApiSecret = apiSecret;
        CreatedAt = createdAt;
    }

    public static ExchangeCredential Create(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        EncryptedSecret apiKey,
        EncryptedSecret apiSecret,
        DateTimeOffset now)
    {
        return new ExchangeCredential(id, ownerId, venue, label, apiKeyLast4, apiKey, apiSecret, now);
    }

    public static ExchangeCredential Restore(
        ExchangeCredentialId id,
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialLabel label,
        ApiKeyLast4 apiKeyLast4,
        EncryptedSecret apiKey,
        EncryptedSecret apiSecret,
        DateTimeOffset createdAt)
    {
        return new ExchangeCredential(id, ownerId, venue, label, apiKeyLast4, apiKey, apiSecret, createdAt);
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }
}