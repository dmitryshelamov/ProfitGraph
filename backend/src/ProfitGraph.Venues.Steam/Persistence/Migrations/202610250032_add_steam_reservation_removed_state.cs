using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     A removed listing keeps its asset reserved until market history shows the item back (ADR 0022): <c>Removed</c> joins
///     the reservation states. It is not final, so the asset stays out of other sells.
/// </summary>
[Migration(202610250032)]
public sealed class AddSteamReservationRemovedStateMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE steam_asset_reservations DROP CONSTRAINT steam_asset_reservations_state_check;
            ALTER TABLE steam_asset_reservations ADD CONSTRAINT steam_asset_reservations_state_check
                CHECK (state IN ('Reserved', 'Listing', 'AwaitingConfirmation', 'Approving', 'Listed', 'Delisting', 'Removed', 'Unknown', 'Released', 'Sold'));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            UPDATE steam_asset_reservations SET state = 'Delisting' WHERE state = 'Removed';
            ALTER TABLE steam_asset_reservations DROP CONSTRAINT steam_asset_reservations_state_check;
            ALTER TABLE steam_asset_reservations ADD CONSTRAINT steam_asset_reservations_state_check
                CHECK (state IN ('Reserved', 'Listing', 'AwaitingConfirmation', 'Approving', 'Listed', 'Delisting', 'Unknown', 'Released', 'Sold'));
            """);
    }
}
