using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Moves the API key and secret of every credential into <c>secrets</c>, a JSON object of encrypted values by field
///     name, so a venue can declare other fields (ADR 0022, C6). Values are moved as stored and never decrypted.
/// </summary>
[Migration(202610250020)]
public sealed class StoreCredentialSecretsByFieldMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE exchange_credentials ADD COLUMN secrets jsonb NULL;

            UPDATE exchange_credentials SET secrets = jsonb_build_object(
                'apiKey', jsonb_build_object(
                    'keyId', api_key_key_id,
                    'nonce', translate(encode(api_key_nonce, 'base64'), E'\n', ''),
                    'ciphertext', translate(encode(api_key_ciphertext, 'base64'), E'\n', ''),
                    'tag', translate(encode(api_key_tag, 'base64'), E'\n', '')),
                'apiSecret', jsonb_build_object(
                    'keyId', api_secret_key_id,
                    'nonce', translate(encode(api_secret_nonce, 'base64'), E'\n', ''),
                    'ciphertext', translate(encode(api_secret_ciphertext, 'base64'), E'\n', ''),
                    'tag', translate(encode(api_secret_tag, 'base64'), E'\n', '')));

            ALTER TABLE exchange_credentials
                ALTER COLUMN secrets SET NOT NULL,
                ADD CONSTRAINT exchange_credentials_secrets_object CHECK (jsonb_typeof(secrets) = 'object' AND secrets <> '{}'::jsonb),
                DROP COLUMN api_key_key_id,
                DROP COLUMN api_key_nonce,
                DROP COLUMN api_key_ciphertext,
                DROP COLUMN api_key_tag,
                DROP COLUMN api_secret_key_id,
                DROP COLUMN api_secret_nonce,
                DROP COLUMN api_secret_ciphertext,
                DROP COLUMN api_secret_tag;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM exchange_credentials WHERE NOT (secrets ?& ARRAY['apiKey', 'apiSecret']) OR (SELECT count(*) FROM jsonb_object_keys(secrets)) <> 2) THEN
                    RAISE EXCEPTION 'Cannot restore key and secret columns while credentials have other fields';
                END IF;
            END $$;

            ALTER TABLE exchange_credentials
                ADD COLUMN api_key_key_id varchar(64) NULL,
                ADD COLUMN api_key_nonce bytea NULL,
                ADD COLUMN api_key_ciphertext bytea NULL,
                ADD COLUMN api_key_tag bytea NULL,
                ADD COLUMN api_secret_key_id varchar(64) NULL,
                ADD COLUMN api_secret_nonce bytea NULL,
                ADD COLUMN api_secret_ciphertext bytea NULL,
                ADD COLUMN api_secret_tag bytea NULL;

            UPDATE exchange_credentials SET
                api_key_key_id = secrets -> 'apiKey' ->> 'keyId',
                api_key_nonce = decode(secrets -> 'apiKey' ->> 'nonce', 'base64'),
                api_key_ciphertext = decode(secrets -> 'apiKey' ->> 'ciphertext', 'base64'),
                api_key_tag = decode(secrets -> 'apiKey' ->> 'tag', 'base64'),
                api_secret_key_id = secrets -> 'apiSecret' ->> 'keyId',
                api_secret_nonce = decode(secrets -> 'apiSecret' ->> 'nonce', 'base64'),
                api_secret_ciphertext = decode(secrets -> 'apiSecret' ->> 'ciphertext', 'base64'),
                api_secret_tag = decode(secrets -> 'apiSecret' ->> 'tag', 'base64');

            ALTER TABLE exchange_credentials
                ALTER COLUMN api_key_key_id SET NOT NULL,
                ALTER COLUMN api_key_nonce SET NOT NULL,
                ALTER COLUMN api_key_ciphertext SET NOT NULL,
                ALTER COLUMN api_key_tag SET NOT NULL,
                ALTER COLUMN api_secret_key_id SET NOT NULL,
                ALTER COLUMN api_secret_nonce SET NOT NULL,
                ALTER COLUMN api_secret_ciphertext SET NOT NULL,
                ALTER COLUMN api_secret_tag SET NOT NULL,
                DROP CONSTRAINT exchange_credentials_secrets_object,
                DROP COLUMN secrets;
            """);
    }
}
