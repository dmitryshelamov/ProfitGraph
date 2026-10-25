using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     An owner's pending link of a new authenticator (ADR 0022): its step and its state encrypted like a credential's
///     secrets, until it expires ten minutes after its last step or its credential is stored.
/// </summary>
[Migration(202610250031)]
public sealed class CreateSteamEnrollmentTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE steam_enrollments (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                step varchar(32) NOT NULL,
                key_id varchar(64) NOT NULL,
                nonce bytea NOT NULL,
                ciphertext bytea NOT NULL,
                tag bytea NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE steam_enrollments;");
    }
}
