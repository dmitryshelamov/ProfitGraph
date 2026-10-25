namespace ProfitGraph.Infrastructure.Accounts.Repositories.Queries;

internal static class ExchangeCredentialQueries
{
    private const string Columns = """
                                   id AS Id, user_id AS UserId, venue AS Venue, label AS Label, api_key_last4 AS ApiKeyLast4,
                                   secrets::text AS Secrets, created_at AS CreatedAt, account_fingerprint AS AccountFingerprint
                                   """;

    internal const string GetById = $"SELECT {Columns} FROM exchange_credentials WHERE id = @id";

    internal const string ListByOwner = $"SELECT {Columns} FROM exchange_credentials WHERE user_id = @ownerId ORDER BY created_at DESC, id";

    internal const string Insert = """
                                   INSERT INTO exchange_credentials (id, user_id, venue, label, api_key_last4, secrets, created_at, account_fingerprint)
                                   VALUES (@Id, @UserId, @Venue, @Label, @ApiKeyLast4, CAST(@Secrets AS jsonb), @CreatedAt, @AccountFingerprint)
                                   ON CONFLICT (venue, account_fingerprint) WHERE account_fingerprint IS NOT NULL DO NOTHING
                                   """;

    internal const string AnyAccountFingerprint =
        "SELECT EXISTS (SELECT 1 FROM exchange_credentials WHERE venue = @venue AND account_fingerprint = ANY(@fingerprints))";

    internal const string Delete = "DELETE FROM exchange_credentials WHERE id = @id AND user_id = @ownerId";
}