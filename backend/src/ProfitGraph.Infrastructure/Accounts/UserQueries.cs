namespace ProfitGraph.Infrastructure.Accounts;

internal static class UserQueries
{
    private const string Columns =
        "id AS Id, login AS Login, password_hash AS PasswordHash, role AS Role, status AS Status, created_at AS CreatedAt, updated_at AS UpdatedAt";

    internal const string GetById = $"SELECT {Columns} FROM users WHERE id = @id";

    internal const string GetByLogin = $"SELECT {Columns} FROM users WHERE lower(login) = lower(@login)";

    internal const string LockActiveOwners = "SELECT id FROM users WHERE role = 'Owner' AND status = 'Active' FOR UPDATE";

    internal const string Insert = """
                                   INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at)
                                   VALUES (@Id, @Login, @PasswordHash, @Role, @Status, @CreatedAt, @UpdatedAt)
                                   ON CONFLICT DO NOTHING
                                   """;

    internal const string InsertFirst = """
                                        LOCK TABLE users IN SHARE ROW EXCLUSIVE MODE;
                                        INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at)
                                        SELECT @Id, @Login, @PasswordHash, @Role, @Status, @CreatedAt, @UpdatedAt
                                        WHERE NOT EXISTS (SELECT 1 FROM users)
                                        """;

    internal const string Update = "UPDATE users SET password_hash = @PasswordHash, status = @Status, updated_at = @UpdatedAt WHERE id = @Id";
}