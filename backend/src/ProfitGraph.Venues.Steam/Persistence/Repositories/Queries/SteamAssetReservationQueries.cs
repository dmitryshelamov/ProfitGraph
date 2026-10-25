namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

internal static class SteamAssetReservationQueries
{
    internal const string ByOrder = """
                                    SELECT client_order_id AS ClientOrderId, credential_id AS CredentialId, app_id AS AppId, context_id AS ContextId,
                                           asset_id AS AssetId, unlock_at AS UnlockAt, seller_minor_units AS SellerMinorUnits, state AS State, listing_id AS ListingId,
                                           created_at AS CreatedAt, updated_at AS UpdatedAt
                                    FROM steam_asset_reservations
                                    WHERE client_order_id = @clientOrderId
                                    ORDER BY unlock_at, app_id, context_id, asset_id
                                    """;

    internal const string OpenByCredential = """
                                             SELECT client_order_id AS ClientOrderId, credential_id AS CredentialId, app_id AS AppId,
                                                    context_id AS ContextId, asset_id AS AssetId, unlock_at AS UnlockAt, seller_minor_units AS SellerMinorUnits, state AS State,
                                                    listing_id AS ListingId, created_at AS CreatedAt, updated_at AS UpdatedAt
                                             FROM steam_asset_reservations
                                             WHERE credential_id = @credentialId AND state NOT IN ('Released', 'Sold')
                                             ORDER BY created_at, client_order_id, asset_id
                                             """;

    internal const string Add = """
                                INSERT INTO steam_asset_reservations (
                                    client_order_id, credential_id, app_id, context_id, asset_id, unlock_at, seller_minor_units, state, listing_id, created_at, updated_at)
                                VALUES (@ClientOrderId, @CredentialId, @AppId, @ContextId, @AssetId, @UnlockAt, @SellerMinorUnits, @State, @ListingId, @CreatedAt, @UpdatedAt)
                                """;

    internal const string Transition = """
                                       UPDATE steam_asset_reservations
                                       SET state = @State, listing_id = @ListingId, updated_at = @UpdatedAt
                                       WHERE client_order_id = @ClientOrderId AND app_id = @AppId AND context_id = @ContextId AND asset_id = @AssetId
                                         AND state = @Expected
                                       """;
}