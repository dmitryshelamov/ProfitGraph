using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     The Steam order journal (ADR 0022, C8): each bot order under its client order ID, with the Steam IDs it maps to. The
///     partial unique index is the single buy-order slot of an item per account: only one buy that Steam may have at a time.
/// </summary>
[Migration(202610250025)]
public sealed class CreateSteamOrderJournalTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE steam_order_journal (
                client_order_id varchar(64) PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                credential_id uuid NOT NULL REFERENCES exchange_credentials (id),
                engine_order_id bigint NOT NULL,
                instrument varchar(256) NOT NULL,
                side varchar(8) NOT NULL CHECK (side IN ('Buy', 'Sell')),
                order_type varchar(8) NOT NULL CHECK (order_type IN ('Limit', 'Market')),
                quantity integer NOT NULL CHECK (quantity > 0),
                price_minor_units bigint NOT NULL CHECK (price_minor_units > 0),
                state varchar(24) NOT NULL
                    CHECK (state IN ('Waiting', 'Placing', 'AwaitingConfirmation', 'Active', 'Cancelling', 'Unknown', 'Done')),
                waiting_code varchar(32) NULL,
                steam_order_id varchar(64) NULL,
                confirmation_creator varchar(64) NULL,
                outcome varchar(16) NULL CHECK (outcome IN ('Rejected', 'Cancelled', 'Filled')),
                place_started_at timestamp with time zone NULL,
                deadline timestamp with time zone NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CHECK ((state = 'Done') = (outcome IS NOT NULL))
            );
            CREATE UNIQUE INDEX ux_steam_order_journal_buy_slot ON steam_order_journal (credential_id, instrument)
                WHERE side = 'Buy' AND state NOT IN ('Waiting', 'Done');
            CREATE INDEX ix_steam_order_journal_open ON steam_order_journal (credential_id) WHERE state <> 'Done';
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE steam_order_journal;");
    }
}
