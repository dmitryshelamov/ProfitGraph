using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610240008)]
public sealed class CreateInstrumentRulesTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE instrument_rules (
                id uuid PRIMARY KEY,
                venue varchar(32) NOT NULL,
                source varchar(32) NOT NULL,
                market varchar(32) NOT NULL,
                symbol varchar(64) NOT NULL,
                base_asset varchar(32) NOT NULL,
                quote_asset varchar(32) NOT NULL,
                base_precision_increment numeric NOT NULL CHECK (base_precision_increment > 0),
                quote_precision_increment numeric NOT NULL CHECK (quote_precision_increment > 0),
                quantity_step numeric NOT NULL CHECK (quantity_step > 0),
                price_tick numeric NOT NULL CHECK (price_tick > 0),
                min_quantity numeric NOT NULL CHECK (min_quantity >= 0),
                min_notional numeric NOT NULL CHECK (min_notional >= 0),
                base_precision integer NOT NULL CHECK (base_precision BETWEEN 0 AND 28),
                quote_precision integer NOT NULL CHECK (quote_precision BETWEEN 0 AND 28),
                buy_fee_asset varchar(8) NOT NULL CHECK (buy_fee_asset IN ('base', 'quote')),
                exact_quote_fees boolean NOT NULL,
                captured_at timestamp with time zone NOT NULL
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE instrument_rules;");
    }
}
