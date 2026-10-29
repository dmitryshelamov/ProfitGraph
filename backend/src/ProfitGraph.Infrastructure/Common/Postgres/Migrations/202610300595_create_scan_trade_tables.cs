using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Scanner trade cards (ADR 0060): each owner's settings per venue, and the trades created from cards, each with its own bot. An
///     owner has at most one live trade on a pair.
/// </summary>
[Migration(202610300595)]
public sealed class CreateScanTradeTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE scan_trade_settings (
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                venue varchar(64) NOT NULL,
                exchange_credential_id uuid NOT NULL REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                risk numeric(38, 18) NOT NULL CHECK (risk > 0),
                max_amount numeric(38, 18) NOT NULL CHECK (max_amount > 0),
                strategy_id uuid NULL REFERENCES strategies (id) ON DELETE SET NULL,
                updated_at timestamp with time zone NOT NULL,
                PRIMARY KEY (owner_id, venue)
            );

            CREATE TABLE scan_trades (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                venue varchar(64) NOT NULL,
                symbol varchar(256) NOT NULL,
                scenario varchar(32) NOT NULL,
                entry numeric(38, 18) NOT NULL,
                stop numeric(38, 18) NOT NULL,
                target numeric(38, 18) NOT NULL,
                amount numeric(38, 18) NOT NULL CHECK (amount > 0),
                risk numeric(38, 18) NOT NULL CHECK (risk > 0),
                bot_id uuid NOT NULL UNIQUE REFERENCES bots (id) ON DELETE CASCADE,
                created_at timestamp with time zone NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                status varchar(32) NOT NULL,
                opened_at timestamp with time zone NULL,
                closed_at timestamp with time zone NULL,
                exit varchar(16) NULL,
                realized_pnl numeric(38, 18) NULL,
                failure_code varchar(64) NULL,
                bot_released boolean NOT NULL,
                CHECK (stop > 0 AND stop < entry AND entry < target)
            );

            CREATE UNIQUE INDEX scan_trades_live_pair ON scan_trades (owner_id, venue, symbol) WHERE status IN ('Waiting', 'Open');
            CREATE INDEX scan_trades_owner_venue_created ON scan_trades (owner_id, venue, created_at DESC);
            CREATE INDEX scan_trades_live ON scan_trades (created_at) WHERE status IN ('Waiting', 'Open');
            CREATE INDEX scan_trades_unreleased ON scan_trades (created_at) WHERE status NOT IN ('Waiting', 'Open') AND NOT bot_released;
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE scan_trades; DROP TABLE scan_trade_settings;");
    }
}
