using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     Which bot order a market history receipt belongs to (ADR 0022, C8; Profit Fox Designer ADR 0087), written once: a
///     receipt is attributed to at most one order and never moved.
/// </summary>
[Migration(202610250030)]
public sealed class CreateSteamMarketAttributionTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE steam_market_attributions (
                credential_id uuid NOT NULL REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                receipt_id varchar(160) NOT NULL,
                client_order_id varchar(64) NOT NULL REFERENCES steam_order_journal (client_order_id) ON DELETE CASCADE,
                bot_id uuid NOT NULL,
                attributed_at timestamp with time zone NOT NULL,
                PRIMARY KEY (credential_id, receipt_id),
                FOREIGN KEY (credential_id, receipt_id) REFERENCES steam_market_receipts (credential_id, receipt_id) ON DELETE CASCADE
            );
            CREATE INDEX ix_steam_market_attributions_order ON steam_market_attributions (client_order_id);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE steam_market_attributions;");
    }
}
