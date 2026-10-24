using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Limits a completed trial must meet to rank: at least <see cref="MinExecutions" /> fills and a drawdown of at most
///     <see cref="MaxDrawdownPercent" />. A trial outside them stays visible as rejected by constraint.
/// </summary>
public sealed record OptimizationConstraints
{
    public long MinExecutions { get; }
    public decimal MaxDrawdownPercent { get; }

    public OptimizationConstraints(long minExecutions = 1, decimal maxDrawdownPercent = 50m)
    {
        if (minExecutions < 0 || maxDrawdownPercent < 0m)
        {
            throw new OptimizationRuleException(OptimizationRuleException.InvalidConstraints, "Constraints cannot be negative.");
        }

        MinExecutions = minExecutions;
        MaxDrawdownPercent = maxDrawdownPercent;
    }

    public bool Accepts(SampleMetrics metrics)
    {
        return metrics.Executions >= MinExecutions && metrics.MaxDrawdownPercent is { } drawdown && drawdown <= MaxDrawdownPercent;
    }
}