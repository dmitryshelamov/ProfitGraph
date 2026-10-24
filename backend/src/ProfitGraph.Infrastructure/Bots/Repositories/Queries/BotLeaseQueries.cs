namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotLeaseQueries
{
    internal const string GetForUpdate = """
                                         SELECT bot_id AS BotId, holder_id AS HolderId, epoch AS Epoch, expires_at AS ExpiresAt,
                                             submissions_ready AS SubmissionsReady
                                         FROM bot_leases WHERE bot_id = @botId FOR UPDATE
                                         """;

    internal const string Insert = """
                                   INSERT INTO bot_leases (bot_id, holder_id, epoch, expires_at, submissions_ready)
                                   VALUES (@BotId, @HolderId, @Epoch, @ExpiresAt, @SubmissionsReady)
                                   """;

    internal const string Update = """
                                   UPDATE bot_leases SET holder_id = @HolderId, epoch = @Epoch, expires_at = @ExpiresAt,
                                       submissions_ready = @SubmissionsReady
                                   WHERE bot_id = @BotId
                                   """;
}