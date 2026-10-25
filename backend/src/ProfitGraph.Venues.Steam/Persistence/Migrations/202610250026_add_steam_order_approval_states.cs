using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     The approval of an entry's mobile confirmation is journaled before and after it is sent (ADR 0022): <c>Approving</c>
///     and <c>Approved</c> join the journal's states. Both hold the item's buy slot.
/// </summary>
[Migration(202610250026)]
public sealed class AddSteamOrderApprovalStatesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE steam_order_journal DROP CONSTRAINT steam_order_journal_state_check;
            ALTER TABLE steam_order_journal ADD CONSTRAINT steam_order_journal_state_check
                CHECK (state IN ('Waiting', 'Placing', 'AwaitingConfirmation', 'Approving', 'Approved', 'Active', 'Cancelling', 'Unknown', 'Done'));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE steam_order_journal DROP CONSTRAINT steam_order_journal_state_check;
            ALTER TABLE steam_order_journal ADD CONSTRAINT steam_order_journal_state_check
                CHECK (state IN ('Waiting', 'Placing', 'AwaitingConfirmation', 'Active', 'Cancelling', 'Unknown', 'Done'));
            """);
    }
}
