using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     An account's value is kept in the currency its venue values it in (ADR 0022); every stored snapshot was in USD.
/// </summary>
[Migration(202610250024)]
public sealed class AddAccountValueCurrencyMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE account_value_snapshots RENAME COLUMN total_value_usd TO total_value;
            ALTER TABLE account_value_snapshots RENAME COLUMN available_value_usd TO available_value;
            ALTER TABLE account_value_snapshots ADD COLUMN value_currency varchar(8) NOT NULL DEFAULT 'USD';
            ALTER TABLE account_value_snapshots ALTER COLUMN value_currency DROP DEFAULT;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DELETE FROM account_value_snapshots WHERE value_currency <> 'USD';
            ALTER TABLE account_value_snapshots DROP COLUMN value_currency;
            ALTER TABLE account_value_snapshots RENAME COLUMN available_value TO available_value_usd;
            ALTER TABLE account_value_snapshots RENAME COLUMN total_value TO total_value_usd;
            """);
    }
}
