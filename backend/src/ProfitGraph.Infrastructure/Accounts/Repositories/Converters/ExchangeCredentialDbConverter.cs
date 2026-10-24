using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Accounts.Repositories.Converters;

internal static class ExchangeCredentialDbConverter
{
    public static ExchangeCredential ToDomain(this ExchangeCredentialDb row)
    {
        return ExchangeCredential.Restore(
            new ExchangeCredentialId(row.Id),
            new UserId(row.UserId),
            new VenueId(row.Venue),
            new ExchangeCredentialLabel(row.Label),
            new ApiKeyLast4(row.ApiKeyLast4),
            new EncryptedSecret(row.ApiKeyKeyId, row.ApiKeyNonce, row.ApiKeyCiphertext, row.ApiKeyTag),
            new EncryptedSecret(row.ApiSecretKeyId, row.ApiSecretNonce, row.ApiSecretCiphertext, row.ApiSecretTag),
            row.CreatedAt);
    }

    public static ExchangeCredentialDb ToDb(this ExchangeCredential credential)
    {
        return new ExchangeCredentialDb
        {
            Id = credential.Id.Value,
            UserId = credential.OwnerId.Value,
            Venue = credential.Venue.Value,
            Label = credential.Label.Value,
            ApiKeyLast4 = credential.ApiKeyLast4.Value,
            ApiKeyKeyId = credential.ApiKey.KeyId,
            ApiKeyNonce = credential.ApiKey.Nonce,
            ApiKeyCiphertext = credential.ApiKey.Ciphertext,
            ApiKeyTag = credential.ApiKey.Tag,
            ApiSecretKeyId = credential.ApiSecret.KeyId,
            ApiSecretNonce = credential.ApiSecret.Nonce,
            ApiSecretCiphertext = credential.ApiSecret.Ciphertext,
            ApiSecretTag = credential.ApiSecret.Tag,
            CreatedAt = credential.CreatedAt
        };
    }
}