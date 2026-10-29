using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The market scanner's current snapshot (ADR 0059): one row per venue and pair, replaced as a whole after each minute; no
///     history is kept. Metrics are exact decimals, empty when the pair lacks the history.
/// </summary>
[Migration(202610300590)]
public sealed class CreateScanSnapshotRowTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE scan_snapshot_rows (
                venue varchar(64) NOT NULL,
                symbol varchar(256) NOT NULL,
                computed_at timestamp with time zone NOT NULL,
                price numeric NULL,
                change_5m numeric NULL,
                change_15m numeric NULL,
                change_1h numeric NULL,
                change_4h numeric NULL,
                change_24h numeric NULL,
                turnover_1h numeric NULL,
                turnover_24h numeric NULL,
                volume_spike numeric NULL,
                natr_24h numeric NULL,
                efficiency_ratio_24h numeric NULL,
                distance_to_high_24h numeric NULL,
                distance_to_low_24h numeric NULL,
                distance_to_high_7d numeric NULL,
                distance_to_low_7d numeric NULL,
                spread numeric NULL,
                listing_age_days integer NULL,
                stale boolean NOT NULL,
                incomplete boolean NOT NULL,
                PRIMARY KEY (venue, symbol)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE scan_snapshot_rows;");
    }
}
