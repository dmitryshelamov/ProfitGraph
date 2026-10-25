namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

internal static class SteamOrderJournalQueries
{
    private const string Columns = """
                                   client_order_id AS ClientOrderId, bot_id AS BotId, credential_id AS CredentialId,
                                   engine_order_id AS EngineOrderId, instrument AS Instrument, side AS Side, order_type AS OrderType,
                                   quantity AS Quantity, price_minor_units AS PriceMinorUnits, state AS State, waiting_code AS WaitingCode,
                                   steam_order_id AS SteamOrderId, confirmation_creator AS ConfirmationCreator, outcome AS Outcome,
                                   place_started_at AS PlaceStartedAt, deadline AS Deadline, created_at AS CreatedAt, updated_at AS UpdatedAt
                                   """;

    internal const string Get = "SELECT " + Columns + " FROM steam_order_journal WHERE client_order_id = @clientOrderId";

    internal const string Open = "SELECT " + Columns + """

                                                       FROM steam_order_journal
                                                       WHERE credential_id = @credentialId AND state <> 'Done'
                                                       ORDER BY created_at, client_order_id
                                                       """;

    internal const string Add = """
                                INSERT INTO steam_order_journal (
                                    client_order_id, bot_id, credential_id, engine_order_id, instrument, side, order_type, quantity,
                                    price_minor_units, state, waiting_code, steam_order_id, confirmation_creator, outcome, place_started_at,
                                    deadline, created_at, updated_at)
                                VALUES (
                                    @ClientOrderId, @BotId, @CredentialId, @EngineOrderId, @Instrument, @Side, @OrderType, @Quantity,
                                    @PriceMinorUnits, @State, @WaitingCode, @SteamOrderId, @ConfirmationCreator, @Outcome, @PlaceStartedAt,
                                    @Deadline, @CreatedAt, @UpdatedAt)
                                ON CONFLICT (client_order_id) DO NOTHING
                                """;

    internal const string Transition = """
                                       UPDATE steam_order_journal
                                       SET state = @State, waiting_code = @WaitingCode, steam_order_id = @SteamOrderId,
                                           confirmation_creator = @ConfirmationCreator, outcome = @Outcome, quantity = @Quantity,
                                           price_minor_units = @PriceMinorUnits, place_started_at = @PlaceStartedAt, deadline = @Deadline,
                                           updated_at = @UpdatedAt
                                       WHERE client_order_id = @ClientOrderId AND state = @Expected
                                       """;
}