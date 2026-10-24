using Dapper;

using Npgsql;

using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Queries;

internal static class StrategyQueries
{
    public static CommandDefinition Add(Strategy strategy, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         INSERT INTO strategies (id, owner_id, name, current_version, created_at, updated_at)
                         VALUES (@Id, @OwnerId, @Name, @CurrentVersion, @CreatedAt, @UpdatedAt)
                         """,
            strategy,
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

    public static CommandDefinition Save(Strategy strategy, int expectedVersion, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         UPDATE strategies SET name = @Name, current_version = @CurrentVersion, updated_at = @UpdatedAt
                         WHERE id = @Id AND current_version = @ExpectedVersion
                         """,
            new { strategy.Id, strategy.Name, strategy.CurrentVersion, strategy.UpdatedAt, ExpectedVersion = expectedVersion },
            transaction,
            cancellationToken: cancellationToken);
    }
}