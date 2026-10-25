using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     The Steam state of a credential (ADR 0022, C8). A fingerprint is unique, so one Steam account has one credential.
/// </summary>
[Migration(202610250021)]
public sealed class CreateSteamAccountTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE steam_accounts (
                credential_id uuid PRIMARY KEY REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                account_fingerprint char(64) NOT NULL UNIQUE,
                guard_status varchar(32) NOT NULL CHECK (guard_status IN ('Checking', 'PendingActivation', 'CoolingDown', 'Ready', 'Restricted')),
                next_check_at timestamp with time zone NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE steam_accounts;");
    }
}
