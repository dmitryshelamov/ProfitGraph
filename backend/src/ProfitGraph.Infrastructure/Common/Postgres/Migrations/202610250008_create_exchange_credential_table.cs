using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250008)]
public sealed class CreateExchangeCredentialTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE exchange_credentials (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES users (id),
                venue varchar(32) NOT NULL,
                label varchar(100) NOT NULL CHECK (length(trim(label)) > 0),
                api_key_last4 varchar(4) NOT NULL CHECK (length(api_key_last4) = 4),
                api_key_key_id varchar(64) NOT NULL,
                api_key_nonce bytea NOT NULL CHECK (octet_length(api_key_nonce) = 12),
                api_key_ciphertext bytea NOT NULL CHECK (octet_length(api_key_ciphertext) > 0),
                api_key_tag bytea NOT NULL CHECK (octet_length(api_key_tag) = 16),
                api_secret_key_id varchar(64) NOT NULL,
                api_secret_nonce bytea NOT NULL CHECK (octet_length(api_secret_nonce) = 12),
                api_secret_ciphertext bytea NOT NULL CHECK (octet_length(api_secret_ciphertext) > 0),
                api_secret_tag bytea NOT NULL CHECK (octet_length(api_secret_tag) = 16),
                created_at timestamp with time zone NOT NULL
            );
            CREATE INDEX exchange_credentials_user_created ON exchange_credentials (user_id, created_at DESC, id);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE exchange_credentials;");
    }
}
