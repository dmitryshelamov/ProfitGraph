namespace ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

internal static class OptimizationRunQueries
{
    public const string Add =
        """
        INSERT INTO optimizations (id, owner_id, strategy_id, strategy_version, created_at, configuration)
        VALUES (@Id, @OwnerId, @StrategyId, @StrategyVersion, @CreatedAt, CAST(@Configuration AS jsonb));
        """;

    public const string Get =
        """
        SELECT o.id AS Id, o.owner_id AS OwnerId, o.strategy_id AS StrategyId, o.strategy_version AS StrategyVersion, o.created_at AS CreatedAt,
            o.configuration::text AS Configuration, o.stage AS Stage, o.planned_trials AS PlannedTrials, o.completed AS Completed,
            o.rejected_by_constraint AS RejectedByConstraint, o.rejected_by_template AS RejectedByTemplate, o.failed AS Failed,
            o.best_trial_index AS BestTrialIndex, o.best_objective AS BestObjective, o.history_identity AS HistoryIdentity, o.history_from AS HistoryFrom,
            o.replay_from AS ReplayFrom, o.replay_to AS ReplayTo, o.warmup_count AS WarmupCount, o.replay_count AS ReplayCount,
            o.rules_snapshot_id AS RulesSnapshotId, o.base_asset AS BaseAsset, o.quote_asset AS QuoteAsset,
            i.quantity_step AS QuantityStep, i.price_tick AS PriceTick, i.min_quantity AS MinQuantity, i.min_notional AS MinNotional,
            i.base_precision AS BasePrecision, i.quote_precision AS QuotePrecision, i.buy_fee_asset AS BuyFeeAsset, i.exact_quote_fees AS ExactQuoteFees
        FROM optimizations o
        LEFT JOIN instrument_rules i ON i.id = o.rules_snapshot_id
        WHERE o.id = @Id;
        """;

    public const string Save =
        """
        UPDATE optimizations SET
            stage = @Stage, history_identity = @HistoryIdentity, history_from = @HistoryFrom, replay_from = @ReplayFrom, replay_to = @ReplayTo,
            warmup_count = @WarmupCount, replay_count = @ReplayCount, rules_snapshot_id = @RulesSnapshotId, base_asset = @BaseAsset,
            quote_asset = @QuoteAsset, planned_trials = @PlannedTrials, completed = @Completed, rejected_by_constraint = @RejectedByConstraint,
            rejected_by_template = @RejectedByTemplate, failed = @Failed, best_trial_index = @BestTrialIndex, best_objective = @BestObjective
        WHERE id = @Id;
        """;
}