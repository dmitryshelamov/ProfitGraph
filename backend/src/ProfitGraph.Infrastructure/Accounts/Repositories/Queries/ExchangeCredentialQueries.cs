namespace ProfitGraph.Infrastructure.Accounts.Repositories.Queries;

internal static class ExchangeCredentialQueries
{
    private const string Columns = """
                                   id AS Id, user_id AS UserId, venue AS Venue, label AS Label, api_key_last4 AS ApiKeyLast4,
                                   api_key_key_id AS ApiKeyKeyId, api_key_nonce AS ApiKeyNonce, api_key_ciphertext AS ApiKeyCiphertext, api_key_tag AS ApiKeyTag,
                                   api_secret_key_id AS ApiSecretKeyId, api_secret_nonce AS ApiSecretNonce, api_secret_ciphertext AS ApiSecretCiphertext,
                                   api_secret_tag AS ApiSecretTag, created_at AS CreatedAt
                                   """;

    internal const string GetById = $"SELECT {Columns} FROM exchange_credentials WHERE id = @id";

    internal const string ListByOwner = $"SELECT {Columns} FROM exchange_credentials WHERE user_id = @ownerId ORDER BY created_at DESC, id";

    internal const string Insert = """
                                   INSERT INTO exchange_credentials (
                                       id, user_id, venue, label, api_key_last4,
                                       api_key_key_id, api_key_nonce, api_key_ciphertext, api_key_tag,
                                       api_secret_key_id, api_secret_nonce, api_secret_ciphertext, api_secret_tag, created_at)
                                   VALUES (
                                       @Id, @UserId, @Venue, @Label, @ApiKeyLast4,
                                       @ApiKeyKeyId, @ApiKeyNonce, @ApiKeyCiphertext, @ApiKeyTag,
                                       @ApiSecretKeyId, @ApiSecretNonce, @ApiSecretCiphertext, @ApiSecretTag, @CreatedAt)
                                   """;

    internal const string Delete = "DELETE FROM exchange_credentials WHERE id = @id AND user_id = @ownerId";
}