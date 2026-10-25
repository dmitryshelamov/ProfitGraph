namespace ProfitGraph.Venues.TInvest.Persistence.Repositories.Queries;

internal static class TInvestOrderAttemptQueries
{
    private const string Columns = """
                                   request_id AS RequestId, bot_id AS BotId, engine_order_id AS EngineOrderId, attempt AS Attempt,
                                   instrument_uid AS InstrumentUid, side AS Side, order_type AS OrderType, lots AS Lots, price AS Price,
                                   state AS State, exchange_order_id AS ExchangeOrderId, broker_code AS BrokerCode,
                                   created_at AS CreatedAt, updated_at AS UpdatedAt
                                   """;

    public const string Add = """
                              INSERT INTO tinvest_order_attempts (
                                  request_id, bot_id, engine_order_id, attempt, instrument_uid, side, order_type, lots, price, state,
                                  exchange_order_id, broker_code, created_at, updated_at)
                              VALUES (
                                  @RequestId, @BotId, @EngineOrderId, @Attempt, @InstrumentUid, @Side, @OrderType, @Lots, @Price, @State,
                                  @ExchangeOrderId, @BrokerCode, @CreatedAt, @UpdatedAt)
                              ON CONFLICT DO NOTHING
                              """;

    public const string Latest = $"""
                                  SELECT {Columns}
                                  FROM tinvest_order_attempts
                                  WHERE bot_id = @botId AND engine_order_id = @engineOrderId
                                  ORDER BY attempt DESC
                                  LIMIT 1
                                  """;

    public const string ByRequestId = $"""
                                       SELECT {Columns}
                                       FROM tinvest_order_attempts
                                       WHERE request_id = @requestId
                                       """;

    public const string Transition = """
                                     UPDATE tinvest_order_attempts
                                     SET state = @State, exchange_order_id = @ExchangeOrderId, broker_code = @BrokerCode, updated_at = @UpdatedAt
                                     WHERE request_id = @RequestId AND state = @Expected
                                     """;

    public const string Owners = """
                                 SELECT request_id AS RequestId, bot_id AS BotId
                                 FROM tinvest_order_attempts
                                 WHERE request_id = ANY(@requestIds)
                                 """;
}