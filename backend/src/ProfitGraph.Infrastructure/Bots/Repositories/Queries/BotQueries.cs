namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotQueries
{
    private const string Columns = """
                                   id AS Id, user_id AS UserId, strategy_id AS StrategyId, strategy_version_id AS StrategyVersionId,
                                   exchange_credential_id AS ExchangeCredentialId, venue AS Venue, symbol AS Symbol, timeframe AS Timeframe,
                                   parameters::text AS Parameters, allocation_cap AS AllocationCap, max_order_quote AS MaxOrderQuote,
                                   daily_loss_limit_quote AS DailyLossLimitQuote, limit_fee_rate AS LimitFeeRate, market_fee_rate AS MarketFeeRate,
                                   fee_rates_read_at AS FeeRatesReadAt, seller_fees::text AS SellerFees, allow_foreign_orders AS AllowForeignOrders, desired_state AS DesiredState,
                                   runtime_status AS RuntimeStatus, status_reason AS StatusReason, diagnostics AS Diagnostics,
                                   restart_attempts AS RestartAttempts, next_restart_at AS NextRestartAt, has_run AS HasRun,
                                   first_started_at AS FirstStartedAt, day_start_utc::timestamp AS DayStartUtc, day_start_equity AS DayStartEquity,
                                   rejected_version_id AS RejectedVersionId, created_at AS CreatedAt, updated_at AS UpdatedAt,
                                   venue_settings_version AS VenueSettingsVersion, venue_settings::text AS VenueSettings, name AS Name
                                   """;

    internal const string GetById = $"SELECT {Columns} FROM bots WHERE id = @id";

    internal const string GetByIdForUpdate = $"SELECT {Columns} FROM bots WHERE id = @id FOR UPDATE";

    internal const string ListByOwner = $"SELECT {Columns} FROM bots WHERE user_id = @ownerId ORDER BY created_at DESC, id";

    internal const string ListDesiredRunning = "SELECT id FROM bots WHERE desired_state = 'Running' ORDER BY id";

    internal const string ListDesiredRunningInstruments = "SELECT DISTINCT venue, symbol FROM bots WHERE desired_state = 'Running' ORDER BY venue, symbol";

    internal const string ListDesiredRunningOnStrategy =
        $"SELECT {Columns} FROM bots WHERE strategy_id = @strategyId AND desired_state = 'Running' ORDER BY created_at, id";

    internal const string ListDesiredRunningOnSeries = """
                                                       SELECT id FROM bots
                                                       WHERE desired_state = 'Running' AND venue = @venue AND symbol = @symbol AND timeframe = @timeframe
                                                       ORDER BY id
                                                       """;

    internal const string ListDesiredRunningOnCredential =
        "SELECT id FROM bots WHERE desired_state = 'Running' AND exchange_credential_id = @credentialId ORDER BY id";

    internal const string AnyUsesCredential = "SELECT EXISTS (SELECT 1 FROM bots WHERE exchange_credential_id = @credentialId)";

    internal const string Insert = """
                                   INSERT INTO bots (
                                       id, user_id, strategy_id, strategy_version_id, exchange_credential_id, venue, symbol, timeframe, parameters,
                                       allocation_cap, max_order_quote, daily_loss_limit_quote, limit_fee_rate, market_fee_rate, fee_rates_read_at, seller_fees,
                                       allow_foreign_orders, desired_state, runtime_status, status_reason, diagnostics, restart_attempts,
                                       next_restart_at, has_run, first_started_at, day_start_utc, day_start_equity, rejected_version_id,
                                       created_at, updated_at, venue_settings_version, venue_settings, name)
                                   VALUES (
                                       @Id, @UserId, @StrategyId, @StrategyVersionId, @ExchangeCredentialId, @Venue, @Symbol, @Timeframe,
                                       CAST(@Parameters AS jsonb), @AllocationCap, @MaxOrderQuote, @DailyLossLimitQuote, @LimitFeeRate, @MarketFeeRate,
                                       @FeeRatesReadAt, CAST(@SellerFees AS jsonb), @AllowForeignOrders, @DesiredState, @RuntimeStatus, @StatusReason, @Diagnostics,
                                       @RestartAttempts, @NextRestartAt, @HasRun, @FirstStartedAt, CAST(@DayStartUtc AS date), @DayStartEquity,
                                       @RejectedVersionId, @CreatedAt, @UpdatedAt, @VenueSettingsVersion, CAST(@VenueSettings AS jsonb), @Name)
                                   """;

    internal const string Update = """
                                   UPDATE bots SET
                                       strategy_version_id = @StrategyVersionId, parameters = CAST(@Parameters AS jsonb), allocation_cap = @AllocationCap,
                                       max_order_quote = @MaxOrderQuote, daily_loss_limit_quote = @DailyLossLimitQuote, limit_fee_rate = @LimitFeeRate,
                                       market_fee_rate = @MarketFeeRate, fee_rates_read_at = @FeeRatesReadAt, seller_fees = CAST(@SellerFees AS jsonb), allow_foreign_orders = @AllowForeignOrders,
                                       desired_state = @DesiredState, runtime_status = @RuntimeStatus, status_reason = @StatusReason,
                                       diagnostics = @Diagnostics, restart_attempts = @RestartAttempts, next_restart_at = @NextRestartAt,
                                       has_run = @HasRun, first_started_at = @FirstStartedAt, day_start_utc = CAST(@DayStartUtc AS date),
                                       day_start_equity = @DayStartEquity, rejected_version_id = @RejectedVersionId, updated_at = @UpdatedAt, name = @Name
                                   WHERE id = @Id
                                   """;
}