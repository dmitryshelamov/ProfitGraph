using Dapper;

using Npgsql;

using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Queries;

internal static class StrategyQueries
{
    public static CommandDefinition Add(StrategyDb row, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         INSERT INTO strategies (id, owner_id, name, current_version, created_at, updated_at)
                         VALUES (@Id, @OwnerId, @Name, @CurrentVersion, @CreatedAt, @UpdatedAt)
                         """,
            row,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Get(Guid id, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         SELECT id AS Id, owner_id AS OwnerId, name AS Name, current_version AS CurrentVersion, created_at AS CreatedAt,
                                updated_at AS UpdatedAt
                         FROM strategies WHERE id = @id
                         """,
            new { id },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition ListAll(NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         SELECT id AS Id, owner_id AS OwnerId, name AS Name, current_version AS CurrentVersion, created_at AS CreatedAt,
                                updated_at AS UpdatedAt
                         FROM strategies
                         ORDER BY created_at ASC, id ASC
                         """,
            transaction: transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition List(Guid ownerId, int limit, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         SELECT s.id AS Id, s.name AS Name, s.current_version AS CurrentVersion, s.updated_at AS UpdatedAt,
                                v.snapshot_json->'definition'->>'templateKind' AS TemplateKind
                         FROM strategies s
                         JOIN strategy_versions v ON v.strategy_id = s.id AND v.version_number = s.current_version
                         WHERE s.owner_id = @ownerId
                         ORDER BY s.updated_at DESC, s.id ASC
                         LIMIT @limit
                         """,
            new { ownerId, limit },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Rename(StrategyDb row, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "UPDATE strategies SET name = @Name, updated_at = @UpdatedAt WHERE id = @Id",
            new { row.Id, row.Name, row.UpdatedAt },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Save(StrategyDb row, int expectedVersion, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         UPDATE strategies SET name = @Name, current_version = @CurrentVersion, updated_at = @UpdatedAt
                         WHERE id = @Id AND current_version = @ExpectedVersion
                         """,
            new { row.Id, row.Name, row.CurrentVersion, row.UpdatedAt, ExpectedVersion = expectedVersion },
            transaction,
            cancellationToken: cancellationToken);
    }
}