using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Records the non-secret identifier a credential shows beside its masked key. Credentials stored earlier keep null; the
///     list reads their identifier from the venue's public field.
/// </summary>
[Migration(202610260003)]
public sealed class AddCredentialPublicIdMigration : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE exchange_credentials ADD COLUMN public_id varchar(128) CHECK (public_id IS NULL OR length(public_id) > 0);");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE exchange_credentials DROP COLUMN public_id;");
    }
}
