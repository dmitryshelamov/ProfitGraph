namespace ProfitGraph.Infrastructure.Accounts.Repositories.Queries;

internal static class ExchangeCredentialQueries
{
    private const string Columns = """
                                   id AS Id, user_id AS UserId, venue AS Venue, label AS Label, api_key_last4 AS ApiKeyLast4,
                                   secrets::text AS Secrets, created_at AS CreatedAt
                                   """;

    internal const string GetById = $"SELECT {Columns} FROM exchange_credentials WHERE id = @id";

    internal const string ListByOwner = $"SELECT {Columns} FROM exchange_credentials WHERE user_id = @ownerId ORDER BY created_at DESC, id";

    internal const string Insert = """
                                   INSERT INTO exchange_credentials (id, user_id, venue, label, api_key_last4, secrets, created_at)
                                   VALUES (@Id, @UserId, @Venue, @Label, @ApiKeyLast4, CAST(@Secrets AS jsonb), @CreatedAt)
                                   """;

    internal const string Delete = "DELETE FROM exchange_credentials WHERE id = @id AND user_id = @ownerId";
}