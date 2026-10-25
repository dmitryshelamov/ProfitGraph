using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250016)]
public sealed class CreateAccountSnapshotTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE account_value_snapshots (
                id uuid PRIMARY KEY,
                credential_id uuid NOT NULL REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                captured_at timestamp with time zone NOT NULL,
                bucket_start timestamp with time zone NOT NULL,
                total_value_usd numeric NOT NULL,
                available_value_usd numeric NULL,
                coins jsonb NOT NULL,
                UNIQUE (credential_id, bucket_start)
            );
            CREATE INDEX account_value_snapshots_credential_captured ON account_value_snapshots (credential_id, captured_at);
            CREATE TABLE account_balance_status (
                credential_id uuid PRIMARY KEY REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                last_attempt_at timestamp with time zone NULL,
                last_success_at timestamp with time zone NULL,
                failure_reason varchar(64) NULL,
                failure_detail varchar(200) NULL,
                consecutive_failures integer NOT NULL DEFAULT 0 CHECK (consecutive_failures >= 0),
                next_attempt_at timestamp with time zone NULL
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE account_balance_status; DROP TABLE account_value_snapshots;");
    }
}
