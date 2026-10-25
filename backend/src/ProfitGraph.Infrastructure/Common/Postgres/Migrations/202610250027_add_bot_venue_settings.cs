using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     A bot stores its venue module's settings without reading them (ADR 0022, C9): their version and the JSON object.
///     Existing bots have none.
/// </summary>
[Migration(202610250027)]
public sealed class AddBotVenueSettingsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE bots
                ADD COLUMN venue_settings_version integer NOT NULL DEFAULT 0 CHECK (venue_settings_version >= 0),
                ADD COLUMN venue_settings jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(venue_settings) = 'object');
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE bots DROP COLUMN venue_settings, DROP COLUMN venue_settings_version;");
    }
}
