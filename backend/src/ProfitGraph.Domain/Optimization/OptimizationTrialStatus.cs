namespace ProfitGraph.Domain.Optimization;

public enum OptimizationTrialStatus
{
    Completed,
    RejectedByConstraint,

    /// <summary>
    ///     The template's own parameter rules forbid the candidate; it is not replayed and spends no budget.
    /// </summary>
    RejectedByTemplate,

    Failed
}