using System.Text.Json;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Accounts.Repositories.Converters;

internal static class ExchangeCredentialDbConverter
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static ExchangeCredential ToDomain(this ExchangeCredentialDb row)
    {
        return ExchangeCredential.Restore(
            new ExchangeCredentialId(row.Id),
            new UserId(row.UserId),
            new VenueId(row.Venue),
            new ExchangeCredentialLabel(row.Label),
            new ApiKeyLast4(row.ApiKeyLast4),
            JsonSerializer.Deserialize<Dictionary<string, EncryptedSecretDb>>(row.Secrets, Options)!
                .ToDictionary(secret => secret.Key, secret => new EncryptedSecret(secret.Value.KeyId, secret.Value.Nonce, secret.Value.Ciphertext, secret.Value.Tag)),
            row.CreatedAt,
            row.AccountFingerprint,
            row.PublicId is null ? null : new CredentialPublicId(row.PublicId));
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
            Secrets = JsonSerializer.Serialize(
                credential.Secrets.ToDictionary(
                    secret => secret.Key,
                    secret => new EncryptedSecretDb
                        { KeyId = secret.Value.KeyId, Nonce = secret.Value.Nonce, Ciphertext = secret.Value.Ciphertext, Tag = secret.Value.Tag }),
                Options),
            CreatedAt = credential.CreatedAt,
            AccountFingerprint = credential.AccountFingerprint,
            PublicId = credential.PublicId?.Value
        };
    }
}