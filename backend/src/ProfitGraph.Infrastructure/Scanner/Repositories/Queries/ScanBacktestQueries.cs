namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanBacktestQueries
{
    private const string Columns = """
                                   id AS Id, owner_id AS OwnerId, venue AS Venue, preset_name AS PresetName, filter::text AS FilterJson,
                                   period_from AS PeriodFrom, period_to AS PeriodTo, job_id AS JobId, created_at AS CreatedAt, completed_at AS CompletedAt,
                                   result::text AS ResultJson
                                   """;

    internal const string Get = $"SELECT {Columns} FROM scan_backtests WHERE id = @Id AND owner_id = @OwnerId";

    internal const string GetForUpdate = $"SELECT {Columns} FROM scan_backtests WHERE id = @Id FOR UPDATE";

    internal const string List = """
                                 SELECT backtest.id AS Id, backtest.preset_name AS PresetName, backtest.period_from AS PeriodFrom,
                                     backtest.period_to AS PeriodTo, backtest.job_id AS JobId, job.status AS JobStatus, backtest.created_at AS CreatedAt,
                                     backtest.completed_at AS CompletedAt, (backtest.result -> 'summary' ->> 'signals')::int AS Signals
                                 FROM scan_backtests AS backtest
                                 LEFT JOIN background_jobs AS job ON job.id = backtest.job_id
                                 WHERE backtest.owner_id = @OwnerId AND backtest.venue = @Venue
                                 ORDER BY backtest.created_at DESC, backtest.id
                                 LIMIT @Limit
                                 """;

    internal const string LatestCardOdds = """
                                           SELECT id AS Id, preset_name AS PresetName, period_from AS PeriodFrom, period_to AS PeriodTo,
                                               completed_at AS CompletedAt, card_odds::text AS CardOddsJson
                                           FROM scan_backtests
                                           WHERE owner_id = @OwnerId AND venue = @Venue AND completed_at IS NOT NULL AND card_odds IS NOT NULL
                                           ORDER BY completed_at DESC, id
                                           LIMIT 1
                                           """;

    internal const string Add = """
                                INSERT INTO scan_backtests (id, owner_id, venue, preset_name, filter, period_from, period_to, job_id, created_at, completed_at, result,
                                    card_odds)
                                VALUES (@Id, @OwnerId, @Venue, @PresetName, CAST(@FilterJson AS jsonb), @PeriodFrom, @PeriodTo, @JobId, @CreatedAt, @CompletedAt,
                                    CAST(@ResultJson AS jsonb), CAST(@CardOddsJson AS jsonb))
                                """;

    internal const string Update = """
                                   UPDATE scan_backtests SET completed_at = @CompletedAt, result = CAST(@ResultJson AS jsonb), card_odds = CAST(@CardOddsJson AS jsonb)
                                   WHERE id = @Id
                                   """;
}