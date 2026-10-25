namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotFeedQueries
{
    internal const string Insert = """
                                   INSERT INTO bot_events (id, bot_id, kind, details, occurred_at)
                                   VALUES (@Id, @BotId, @Kind, CAST(@Details AS jsonb), @OccurredAt)
                                   """;

    internal const string List = """
                                 SELECT id AS Id, sequence AS Sequence, bot_id AS BotId, kind AS Kind, details::text AS Details, occurred_at AS OccurredAt
                                 FROM bot_events
                                 WHERE bot_id = @botId AND (@before::bigint IS NULL OR sequence < @before)
                                 ORDER BY sequence DESC
                                 LIMIT @limit
                                 """;

    internal const string DeleteOlderThan = "DELETE FROM bot_events WHERE occurred_at < @cutoff";
}