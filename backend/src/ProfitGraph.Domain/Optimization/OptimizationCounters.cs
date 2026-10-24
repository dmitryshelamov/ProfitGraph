namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     Stored trials by status. Template rejections are not executed and do not count against the budget.
/// </summary>
public sealed record OptimizationCounters(int Completed, int RejectedByConstraint, int RejectedByTemplate, int Failed)
{
    public static OptimizationCounters None { get; } = new OptimizationCounters(Completed: 0, RejectedByConstraint: 0, RejectedByTemplate: 0, Failed: 0);

    /// <summary>
    ///     Executed trials, whatever their status.
    /// </summary>
    public int Executed => Completed + RejectedByConstraint + Failed;

    public OptimizationCounters Add(OptimizationTrialStatus status)
    {
        return status switch
        {
            OptimizationTrialStatus.Completed => this with { Completed = Completed + 1 },
            OptimizationTrialStatus.RejectedByConstraint => this with { RejectedByConstraint = RejectedByConstraint + 1 },
            OptimizationTrialStatus.RejectedByTemplate => this with { RejectedByTemplate = RejectedByTemplate + 1 },
            OptimizationTrialStatus.Failed => this with { Failed = Failed + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}