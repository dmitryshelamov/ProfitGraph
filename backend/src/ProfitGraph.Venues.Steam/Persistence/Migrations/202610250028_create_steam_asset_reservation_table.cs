using FluentMigrator;

namespace ProfitGraph.Venues.Steam.Persistence.Migrations;

/// <summary>
///     The items a bot's sell lists, one row per asset (ADR 0022, C8): an asset is reserved by at most one open sell of the
///     account, and each row carries the asset's Steam listing once it has one.
/// </summary>
[Migration(202610250028)]
public sealed class CreateSteamAssetReservationTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE steam_asset_reservations (
                client_order_id varchar(64) NOT NULL REFERENCES steam_order_journal (client_order_id) ON DELETE CASCADE,
                credential_id uuid NOT NULL REFERENCES exchange_credentials (id),
                app_id integer NOT NULL CHECK (app_id > 0),
                context_id varchar(20) NOT NULL,
                asset_id varchar(32) NOT NULL,
                unlock_at timestamp with time zone NOT NULL,
                seller_minor_units bigint NOT NULL CHECK (seller_minor_units > 0),
                state varchar(24) NOT NULL
                    CHECK (state IN ('Reserved', 'Listing', 'AwaitingConfirmation', 'Approving', 'Listed', 'Delisting', 'Unknown', 'Released', 'Sold')),
                listing_id varchar(64) NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                PRIMARY KEY (client_order_id, app_id, context_id, asset_id)
            );
            CREATE UNIQUE INDEX ux_steam_asset_reservations_asset ON steam_asset_reservations (credential_id, app_id, context_id, asset_id)
                WHERE state NOT IN ('Released', 'Sold');
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE steam_asset_reservations;");
    }
}
