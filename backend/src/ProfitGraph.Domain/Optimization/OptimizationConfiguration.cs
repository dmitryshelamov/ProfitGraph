using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Everything an optimization freezes when it starts (stage 4 plan, S4-2): the backtest it searches around, with the
///     fixed parameter values and the fingerprint of the graph compiled with its ranges, the ranges by parameter name and
///     how the search is run. A parameter is either fixed or ranged, never both.
/// </summary>
public sealed record OptimizationConfiguration
{
    public const int MaximumFinalists = 10;

    public BacktestConfiguration Backtest { get; }
    public IReadOnlyDictionary<string, DecimalOptimizeParamValue> Ranges { get; }
    public OptimizationSearchMethod Method { get; }
    public OptimizationObjective Objective { get; }
    public OptimizationConstraints Constraints { get; }
    public int Budget { get; }
    public ulong Seed { get; }
    public int FinalistCount { get; }

    public OptimizationConfiguration(
        BacktestConfiguration backtest,
        IReadOnlyDictionary<string, DecimalOptimizeParamValue> ranges,
        OptimizationSearchMethod method,
        OptimizationObjective objective,
        OptimizationConstraints constraints,
        int budget,
        ulong seed,
        int finalistCount)
    {
        Backtest = backtest ?? throw new ArgumentNullException(nameof(backtest));
        Constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));

        if (ranges.Count == 0 || ranges.Keys.Any(name => string.IsNullOrWhiteSpace(name) || backtest.Parameters.ContainsKey(name)))
        {
            throw Invalid("An optimization needs at least one range, each on a named parameter that is not also fixed.");
        }

        if (!Enum.IsDefined(method) || !Enum.IsDefined(objective))
        {
            throw Invalid("The search method and objective must be supported.");
        }

        if (budget is < 1 or > OptimizationSearchSpace.MaximumBudget || finalistCount is < 0 or > MaximumFinalists)
        {
            throw Invalid($"The budget must be 1 to {OptimizationSearchSpace.MaximumBudget} trials and the finalists 0 to {MaximumFinalists}.");
        }

        Ranges = new SortedDictionary<string, DecimalOptimizeParamValue>(ranges.ToDictionary(), StringComparer.Ordinal).AsReadOnly();
        Method = method;
        Objective = objective;
        Budget = budget;
        Seed = seed;
        FinalistCount = finalistCount;
    }

    private static OptimizationRuleException Invalid(string message)
    {
        return new OptimizationRuleException(OptimizationRuleException.InvalidConfiguration, message);
    }
}