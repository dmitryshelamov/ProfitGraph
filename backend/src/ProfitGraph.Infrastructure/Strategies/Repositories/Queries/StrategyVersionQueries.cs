using Dapper;

using Npgsql;

using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Queries;

internal static class StrategyVersionQueries
{
    public static CommandDefinition Add(StrategyVersionDb row, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         INSERT INTO strategy_versions (id, strategy_id, version_number, snapshot_json, created_at)
                         VALUES (@Id, @StrategyId, @Number, CAST(@SnapshotJson AS jsonb), @CreatedAt)
                         """,
            row,
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Get(Guid id, int number, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         SELECT id AS Id, strategy_id AS StrategyId, version_number AS Number, snapshot_json::text AS SnapshotJson, created_at AS CreatedAt
                         FROM strategy_versions WHERE strategy_id = @id AND version_number = @number
                         """,
            new { id, number },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition ListMetadata(Guid id, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "SELECT id AS Id, version_number AS Number, created_at AS CreatedAt FROM strategy_versions WHERE strategy_id=@id ORDER BY version_number DESC",
            new { id },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetAll(Guid id, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            commandText: """
                         SELECT id AS Id, strategy_id AS StrategyId, version_number AS Number, snapshot_json::text AS SnapshotJson, created_at AS CreatedAt
                         FROM strategy_versions WHERE strategy_id = @id ORDER BY version_number DESC
                         """,
            new { id },
            transaction,
            cancellationToken: cancellationToken);
    }
}