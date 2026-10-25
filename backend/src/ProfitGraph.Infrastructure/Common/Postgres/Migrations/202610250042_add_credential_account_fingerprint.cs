using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Records the keyed fingerprint of the venue account a credential selects, unique per venue, so one venue account is
///     stored once (ADR 0023, TC5). Credentials without account selection keep null.
/// </summary>
[Migration(202610250042)]
public sealed class AddCredentialAccountFingerprintMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE exchange_credentials ADD COLUMN account_fingerprint varchar(128);
            CREATE UNIQUE INDEX exchange_credentials_venue_account ON exchange_credentials (venue, account_fingerprint)
                WHERE account_fingerprint IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX exchange_credentials_venue_account; ALTER TABLE exchange_credentials DROP COLUMN account_fingerprint;");
    }
}
