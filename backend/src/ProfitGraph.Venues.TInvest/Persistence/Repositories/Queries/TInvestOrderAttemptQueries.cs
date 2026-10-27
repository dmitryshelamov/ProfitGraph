namespace ProfitGraph.Venues.TInvest.Persistence.Repositories.Queries;

internal static class TInvestOrderAttemptQueries
{
    private const string Columns = """
                                   request_id AS RequestId, bot_id AS BotId, engine_order_id AS EngineOrderId, attempt AS Attempt,
                                   instrument_uid AS InstrumentUid, side AS Side, order_type AS OrderType, lots AS Lots, price AS Price,
                                   state AS State, exchange_order_id AS ExchangeOrderId, broker_code AS BrokerCode,
                                   created_at AS CreatedAt, updated_at AS UpdatedAt, waiting_code AS WaitingCode, band_low AS BandLow,
                                   band_high AS BandHigh, cancelled_by AS CancelledBy, carried_from::timestamp AS CarriedFrom, executed_lots AS ExecutedLots,
                                   executed_quote AS ExecutedQuote, executed_fee AS ExecutedFee
                                   """;

    public const string Add = """
                              INSERT INTO tinvest_order_attempts (
                                  request_id, bot_id, engine_order_id, attempt, instrument_uid, side, order_type, lots, price, state,
                                  exchange_order_id, broker_code, created_at, updated_at, waiting_code, band_low, band_high, cancelled_by, carried_from,
                                  executed_lots, executed_quote, executed_fee)
                              VALUES (
                                  @RequestId, @BotId, @EngineOrderId, @Attempt, @InstrumentUid, @Side, @OrderType, @Lots, @Price, @State,
                                  @ExchangeOrderId, @BrokerCode, @CreatedAt, @UpdatedAt, @WaitingCode, @BandLow, @BandHigh, @CancelledBy, @CarriedFrom,
                                  @ExecutedLots, @ExecutedQuote, @ExecutedFee)
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
                                     SET state = @State, exchange_order_id = @ExchangeOrderId, broker_code = @BrokerCode, price = @Price,
                                         waiting_code = @WaitingCode, band_low = @BandLow, band_high = @BandHigh, cancelled_by = @CancelledBy, carried_from = @CarriedFrom,
                                         executed_lots = @ExecutedLots, executed_quote = @ExecutedQuote, executed_fee = @ExecutedFee,
                                         updated_at = @UpdatedAt
                                     WHERE request_id = @RequestId AND state = @Expected
                                     """;

    public const string Waiting = $"""
                                   SELECT {Columns}
                                   FROM tinvest_order_attempts
                                   WHERE waiting_code IS NOT NULL
                                   ORDER BY bot_id, engine_order_id
                                   """;

    public const string Working = $"""
                                   SELECT {Columns}
                                   FROM tinvest_order_attempts
                                   WHERE state = 'Active'
                                   ORDER BY bot_id, engine_order_id
                                   """;

    public const string ForOrder = $"""
                                    SELECT {Columns}
                                    FROM tinvest_order_attempts
                                    WHERE bot_id = @botId AND engine_order_id = @engineOrderId
                                    ORDER BY attempt
                                    """;

    public const string ExecutedOnShare = $"""
                                           SELECT {Columns}
                                           FROM tinvest_order_attempts
                                           WHERE bot_id = @botId AND instrument_uid = @instrumentUid AND executed_lots > 0 AND updated_at >= @since
                                           ORDER BY engine_order_id, attempt
                                           """;

    public const string PendingFees = $"""
                                       SELECT {Columns}
                                       FROM tinvest_order_attempts
                                       WHERE state = 'Final' AND executed_lots > 0 AND executed_fee = 0 AND updated_at >= @since
                                       ORDER BY bot_id, engine_order_id, attempt
                                       """;

    public const string Owners = """
                                 SELECT request_id AS RequestId, bot_id AS BotId
                                 FROM tinvest_order_attempts
                                 WHERE request_id = ANY(@requestIds)
                                 """;
}