namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotAlertQueries
{
    private const string Columns = """
                                   id AS Id, bot_id AS BotId, owner_id AS OwnerId, kind AS Kind, reason AS Reason, created_at AS CreatedAt,
                                       acknowledged_at AS AcknowledgedAt
                                   """;

    internal const string GetForUpdate = $"SELECT {Columns} FROM bot_alerts WHERE id = @id FOR UPDATE";

    internal const string List = $"""
                                  SELECT {Columns} FROM bot_alerts
                                  WHERE (@ownerId::uuid IS NULL OR owner_id = @ownerId) AND (@botId::uuid IS NULL OR bot_id = @botId)
                                      AND (NOT @unacknowledgedOnly OR acknowledged_at IS NULL)
                                  ORDER BY created_at DESC, id
                                  LIMIT @limit
                                  """;

    internal const string Insert = """
                                   INSERT INTO bot_alerts (id, bot_id, owner_id, kind, reason, created_at, acknowledged_at)
                                   VALUES (@Id, @BotId, @OwnerId, @Kind, @Reason, @CreatedAt, @AcknowledgedAt)
                                   """;

    internal const string Update = "UPDATE bot_alerts SET acknowledged_at = @AcknowledgedAt WHERE id = @Id";
}