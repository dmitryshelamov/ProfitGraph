namespace ProfitGraph.Application.Backtesting;

public static class BacktestErrorCodes
{
    public const string RunNotFound = "backtest_not_found";
    public const string StrategyInvalid = "strategy_invalid";
    public const string UnknownParameter = "unknown_parameter";
    public const string ParameterFixedAndRanged = "parameter_fixed_and_ranged";
    public const string InvalidSplit = "invalid_split";
    public const string GridProfitBelowFees = "grid_profit_below_fees";

    /// <summary>
    ///     The strategy version no longer compiles to the frozen fingerprint, for example after an engine change.
    /// </summary>
    public const string CompilationChanged = "backtest_compilation_changed";

    public const string ReplayFailed = "backtest_replay_failed";
    public const string PersistenceFailed = "backtest_persistence_failed";
}