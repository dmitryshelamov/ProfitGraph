namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

internal static class SteamEnrollmentQueries
{
    internal const string Get = """
                                SELECT id AS Id, owner_id AS OwnerId, step AS Step, key_id AS KeyId, nonce AS Nonce, ciphertext AS Ciphertext, tag AS Tag,
                                       expires_at AS ExpiresAt, created_at AS CreatedAt, updated_at AS UpdatedAt
                                FROM steam_enrollments WHERE id = @id
                                """;

    internal const string Add = """
                                INSERT INTO steam_enrollments (id, owner_id, step, key_id, nonce, ciphertext, tag, expires_at, created_at, updated_at)
                                VALUES (@Id, @OwnerId, @Step, @KeyId, @Nonce, @Ciphertext, @Tag, @ExpiresAt, @CreatedAt, @UpdatedAt)
                                """;

    internal const string Transition = """
                                       UPDATE steam_enrollments
                                       SET step = @Step, key_id = @KeyId, nonce = @Nonce, ciphertext = @Ciphertext, tag = @Tag, expires_at = @ExpiresAt,
                                           updated_at = @UpdatedAt
                                       WHERE id = @Id AND step = @Expected
                                       """;

    internal const string Delete = "DELETE FROM steam_enrollments WHERE id = @id";
}