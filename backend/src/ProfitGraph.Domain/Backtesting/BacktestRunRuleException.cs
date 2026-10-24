using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     A run operation that the run rules forbid, such as attaching history twice.
/// </summary>
public sealed class BacktestRunRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidConfiguration = "invalid_backtest_configuration";
    public const string InvalidTransition = "invalid_backtest_transition";
    public const string InvalidHistory = "invalid_backtest_history";
    public const string InvalidResult = "invalid_backtest_result";
}