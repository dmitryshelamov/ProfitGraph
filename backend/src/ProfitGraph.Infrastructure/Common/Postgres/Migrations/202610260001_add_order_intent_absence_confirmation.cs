using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     When the owner confirmed a submit absent at the venue, and who (Profit Fox Designer ADR 0055). Existing intents have
///     no confirmation.
/// </summary>
[Migration(202610260001)]
public sealed class AddOrderIntentAbsenceConfirmationMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_order_intents
                ADD COLUMN confirmed_absent_at timestamp with time zone NULL,
                ADD COLUMN confirmed_absent_by uuid NULL REFERENCES users (id),
                ADD CONSTRAINT bot_order_intents_absence_confirmation CHECK ((confirmed_absent_at IS NULL) = (confirmed_absent_by IS NULL));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_order_intents
                DROP CONSTRAINT bot_order_intents_absence_confirmation,
                DROP COLUMN confirmed_absent_by,
                DROP COLUMN confirmed_absent_at;
            """);
    }
}
