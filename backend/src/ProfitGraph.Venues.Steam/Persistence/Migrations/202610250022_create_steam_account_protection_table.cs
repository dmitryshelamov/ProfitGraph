using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     An open protection of a Steam account (ADR 0022, C7); a credential without a row is clear.
/// </summary>
[Migration(202610250022)]
public sealed class CreateSteamAccountProtectionTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE steam_account_protection (
                credential_id uuid PRIMARY KEY REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                reason varchar(32) NOT NULL,
                opened_at timestamp with time zone NOT NULL
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE steam_account_protection;");
    }
}
