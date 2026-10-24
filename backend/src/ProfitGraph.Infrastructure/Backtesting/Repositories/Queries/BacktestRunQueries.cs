namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;

internal static class BacktestRunQueries
{
    public const string Add =
        """
        INSERT INTO backtest_runs (id, owner_id, strategy_id, strategy_version, created_at, configuration)
        VALUES (@Id, @OwnerId, @StrategyId, @StrategyVersion, @CreatedAt, CAST(@Configuration AS jsonb));
        """;

    public const string Get =
        """
        SELECT r.id AS Id, r.owner_id AS OwnerId, r.strategy_id AS StrategyId, r.strategy_version AS StrategyVersion, r.created_at AS CreatedAt,
            r.configuration::text AS Configuration, r.stage AS Stage, r.history_identity AS HistoryIdentity, r.history_from AS HistoryFrom,
            r.replay_from AS ReplayFrom, r.replay_to AS ReplayTo, r.warmup_count AS WarmupCount, r.replay_count AS ReplayCount,
            r.rules_snapshot_id AS RulesSnapshotId, r.base_asset AS BaseAsset, r.quote_asset AS QuoteAsset,
            i.quantity_step AS QuantityStep, i.price_tick AS PriceTick, i.min_quantity AS MinQuantity, i.min_notional AS MinNotional,
            i.base_precision AS BasePrecision, i.quote_precision AS QuotePrecision, i.buy_fee_asset AS BuyFeeAsset, i.exact_quote_fees AS ExactQuoteFees,
            r.outcome AS Outcome, r.processed_candles AS ProcessedCandles, r.equity AS Equity, r.total_pnl AS TotalPnl,
            r.failure_code AS FailureCode, r.failure_message AS FailureMessage, r.metrics::text AS Metrics
        FROM backtest_runs r
        LEFT JOIN instrument_rules i ON i.id = r.rules_snapshot_id
        WHERE r.id = @Id;
        """;

    public const string Save =
        """
        UPDATE backtest_runs SET
            stage = @Stage, history_identity = @HistoryIdentity, history_from = @HistoryFrom, replay_from = @ReplayFrom, replay_to = @ReplayTo,
            warmup_count = @WarmupCount, replay_count = @ReplayCount, rules_snapshot_id = @RulesSnapshotId, base_asset = @BaseAsset,
            quote_asset = @QuoteAsset, outcome = @Outcome, processed_candles = @ProcessedCandles, equity = @Equity, total_pnl = @TotalPnl,
            failure_code = @FailureCode, failure_message = @FailureMessage, metrics = CAST(@Metrics AS jsonb), return_percent = @ReturnPercent
        WHERE id = @Id;

        SELECT pg_notify('backtest_progress', @Id::text);
        """;
}