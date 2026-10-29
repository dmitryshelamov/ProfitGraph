using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Short scanner trades (ADR 0061): a trade's direction, its levels' order by direction, and the owner's short strategy and
///     leverage per venue. Existing trades are long.
/// </summary>
[Migration(202610300610)]
public sealed class AddScanTradeDirectionMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_trades ADD COLUMN direction varchar(8) NOT NULL DEFAULT 'Long';
            ALTER TABLE scan_trades DROP CONSTRAINT scan_trades_check;
            ALTER TABLE scan_trades ADD CONSTRAINT scan_trades_levels CHECK (
                (direction = 'Long' AND stop > 0 AND stop < entry AND entry < target) OR
                (direction = 'Short' AND target > 0 AND target < entry AND entry < stop));

            ALTER TABLE scan_trade_settings
                ADD COLUMN short_strategy_id uuid NULL REFERENCES strategies (id) ON DELETE SET NULL,
                ADD COLUMN leverage numeric(38, 18) NULL CHECK (leverage >= 1);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE scan_trade_settings DROP COLUMN leverage, DROP COLUMN short_strategy_id;
            DELETE FROM scan_trades WHERE direction = 'Short';
            ALTER TABLE scan_trades DROP CONSTRAINT scan_trades_levels;
            ALTER TABLE scan_trades ADD CONSTRAINT scan_trades_check CHECK (stop > 0 AND stop < entry AND entry < target);
            ALTER TABLE scan_trades DROP COLUMN direction;
            """);
    }
}
