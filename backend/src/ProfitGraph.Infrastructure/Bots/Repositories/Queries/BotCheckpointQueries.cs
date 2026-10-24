namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotCheckpointQueries
{
    internal const string Get = """
                                SELECT bot_id AS BotId, schema_version AS SchemaVersion, checkpoint::text AS Checkpoint,
                                    applied_parameters::text AS AppliedParameters, updated_at AS UpdatedAt
                                FROM bot_checkpoints WHERE bot_id = @botId
                                """;

    internal const string Upsert = """
                                   INSERT INTO bot_checkpoints (bot_id, schema_version, checkpoint, applied_parameters, updated_at)
                                   VALUES (@BotId, @SchemaVersion, CAST(@Checkpoint AS jsonb), CAST(@AppliedParameters AS jsonb), @UpdatedAt)
                                   ON CONFLICT (bot_id) DO UPDATE SET
                                       schema_version = EXCLUDED.schema_version, checkpoint = EXCLUDED.checkpoint,
                                       applied_parameters = EXCLUDED.applied_parameters, updated_at = EXCLUDED.updated_at
                                   """;

    internal const string Delete = "DELETE FROM bot_checkpoints WHERE bot_id = @botId";
}