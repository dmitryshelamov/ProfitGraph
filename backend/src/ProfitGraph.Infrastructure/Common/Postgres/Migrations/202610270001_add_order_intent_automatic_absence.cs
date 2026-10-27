using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Marks an order intent that the venue's gateway closed on its own evidence instead of the owner's confirmation (ADR 0030):
///     such an intent has a confirmation time and no confirming user. Existing intents were not closed automatically.
/// </summary>
[Migration(202610270001)]
public sealed class AddOrderIntentAutomaticAbsenceMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_order_intents
                ADD COLUMN confirmed_absent_automatically boolean NOT NULL DEFAULT false,
                DROP CONSTRAINT bot_order_intents_absence_confirmation,
                ADD CONSTRAINT bot_order_intents_absence_confirmation CHECK (
                    CASE WHEN confirmed_absent_automatically
                        THEN confirmed_absent_at IS NOT NULL AND confirmed_absent_by IS NULL
                        ELSE (confirmed_absent_at IS NULL) = (confirmed_absent_by IS NULL)
                    END);
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            UPDATE bot_order_intents SET confirmed_absent_at = NULL WHERE confirmed_absent_automatically;
            ALTER TABLE bot_order_intents
                DROP CONSTRAINT bot_order_intents_absence_confirmation,
                ADD CONSTRAINT bot_order_intents_absence_confirmation CHECK ((confirmed_absent_at IS NULL) = (confirmed_absent_by IS NULL)),
                DROP COLUMN confirmed_absent_automatically;
            """);
    }
}
