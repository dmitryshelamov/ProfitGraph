using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     A strategy operation that the strategy rules forbid, such as an empty name.
/// </summary>
public sealed class StrategyRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidName = "invalid_strategy_name";
}