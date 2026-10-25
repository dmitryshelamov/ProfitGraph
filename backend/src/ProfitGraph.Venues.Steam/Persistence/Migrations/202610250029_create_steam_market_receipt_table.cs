using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     An account's market history receipts at their absolute positions (ADR 0022, C8; Profit Fox Designer ADR 0087),
///     written once: a later read must find the same receipt at the same position.
/// </summary>
[Migration(202610250029)]
public sealed class CreateSteamMarketReceiptTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE steam_market_receipts (
                credential_id uuid NOT NULL REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                position bigint NOT NULL CHECK (position >= 0),
                receipt_id varchar(160) NOT NULL,
                receipt jsonb NOT NULL,
                PRIMARY KEY (credential_id, position),
                UNIQUE (credential_id, receipt_id)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE steam_market_receipts;");
    }
}
