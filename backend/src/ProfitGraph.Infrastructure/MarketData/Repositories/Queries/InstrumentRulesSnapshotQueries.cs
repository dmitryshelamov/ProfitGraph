namespace ProfitGraph.Infrastructure.MarketData.Repositories.Queries;

internal static class InstrumentRulesSnapshotQueries
{
    public const string Add =
        """
        INSERT INTO instrument_rules (
            id, venue, source, market, symbol, base_asset, quote_asset, base_precision_increment, quote_precision_increment,
            quantity_step, price_tick, min_quantity, min_notional, base_precision, quote_precision, buy_fee_asset, exact_quote_fees, captured_at)
        VALUES (
            @Id, @Venue, @Source, @Market, @Symbol, @BaseAsset, @QuoteAsset, @BasePrecisionIncrement, @QuotePrecisionIncrement,
            @QuantityStep, @PriceTick, @MinQuantity, @MinNotional, @BasePrecision, @QuotePrecision, @BuyFeeAsset, @ExactQuoteFees, @CapturedAt);
        """;

    public const string Get =
        """
        SELECT id AS Id, venue AS Venue, source AS Source, market AS Market, symbol AS Symbol, base_asset AS BaseAsset, quote_asset AS QuoteAsset,
            base_precision_increment AS BasePrecisionIncrement, quote_precision_increment AS QuotePrecisionIncrement,
            quantity_step AS QuantityStep, price_tick AS PriceTick, min_quantity AS MinQuantity, min_notional AS MinNotional,
            base_precision AS BasePrecision, quote_precision AS QuotePrecision, buy_fee_asset AS BuyFeeAsset,
            exact_quote_fees AS ExactQuoteFees, captured_at AS CapturedAt
        FROM instrument_rules
        WHERE id = @Id;
        """;
}