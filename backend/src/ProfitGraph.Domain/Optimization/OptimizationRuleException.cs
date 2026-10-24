using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     An optimization request that the search rules forbid, such as an empty search space or a range that decimal cannot
///     hold exactly.
/// </summary>
public sealed class OptimizationRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidRange = "invalid_optimization_range";
    public const string InvalidSearchSpace = "invalid_search_space";
    public const string InvalidBudget = "invalid_optimization_budget";
    public const string InvalidConstraints = "invalid_optimization_constraints";
}