using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Users' own market scanner presets (ADR 0059): a name unique per owner and venue regardless of case, and a versioned JSON
///     definition of conditions, sort and stale switch. Built-in presets live in code.
/// </summary>
[Migration(202610300591)]
public sealed class CreateScanPresetTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE scan_presets (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                venue varchar(64) NOT NULL,
                name varchar(100) NOT NULL,
                definition jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );

            CREATE UNIQUE INDEX scan_presets_owner_venue_name ON scan_presets (owner_id, venue, lower(name));
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE scan_presets;");
    }
}
