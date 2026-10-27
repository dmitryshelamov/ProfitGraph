using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Caches the venue account a credential trades on, as the venue last described it (ADR 0049), so the bot page never reads
///     the venue. It holds no secret: no key, token or signature, only what the venue tells about the account and the key.
/// </summary>
[Migration(202610280040)]
public sealed class CreateAccountIdentityTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE account_identities (
                credential_id uuid PRIMARY KEY REFERENCES exchange_credentials (id) ON DELETE CASCADE,
                account_id varchar(128) NULL,
                account_name varchar(128) NULL,
                account_kind varchar(128) NULL,
                access_level varchar(128) NULL,
                read_only boolean NULL,
                permissions text[] NOT NULL DEFAULT '{}',
                expires_at timestamp with time zone NULL,
                read_at timestamp with time zone NULL,
                last_attempt_at timestamp with time zone NULL,
                failure_reason varchar(64) NULL,
                failure_detail varchar(200) NULL,
                next_attempt_at timestamp with time zone NULL
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE account_identities;");
    }
}
