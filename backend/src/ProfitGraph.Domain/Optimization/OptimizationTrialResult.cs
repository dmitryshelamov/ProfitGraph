using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     How a trial ended and, when it completed within the constraints, its objective value.
/// </summary>
public sealed class OptimizationTrialResult
{
    public OptimizationTrial Trial { get; }
    public SampleMetrics? Metrics { get; }
    public OptimizationTrialStatus Status { get; }
    public decimal? ObjectiveValue { get; }
    public string? FailureCode { get; }
    public long ElapsedMs { get; }

    private OptimizationTrialResult(
        OptimizationTrial trial,
        SampleMetrics? metrics,
        OptimizationTrialStatus status,
        decimal? objectiveValue,
        string? failureCode,
        long elapsedMs)
    {
        Trial = trial;
        Metrics = metrics;
        Status = status;
        ObjectiveValue = objectiveValue;
        FailureCode = failureCode;
        ElapsedMs = elapsedMs;
    }

    public static OptimizationTrialResult Evaluate(
        OptimizationTrial trial,
        SampleMetrics metrics,
        OptimizationObjective objective,
        OptimizationConstraints constraints,
        long elapsedMs = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMs);

        if (!Enum.IsDefined(objective))
        {
            throw new ArgumentOutOfRangeException(nameof(objective));
        }

        OptimizationTrialStatus status = constraints.Accepts(metrics) ? OptimizationTrialStatus.Completed : OptimizationTrialStatus.RejectedByConstraint;
        decimal? value = null;

        if (status == OptimizationTrialStatus.Completed)
        {
            try
            {
                value = objective switch
                {
                    OptimizationObjective.InSampleReturnPercent => metrics.ReturnPercent,
                    OptimizationObjective.InSampleTotalPnl => metrics.TotalPnl,
                    OptimizationObjective.InSampleReturnToDrawdown when metrics.MaxDrawdownPercent > 0m => metrics.ReturnPercent / metrics.MaxDrawdownPercent,
                    _ => null
                };
            }
            catch (OverflowException)
            {
                // An unrepresentable derived metric is unavailable, as in backtest metrics.
                value = null;
            }
        }

        return new OptimizationTrialResult(trial, metrics, status, value, failureCode: null, elapsedMs);
    }

    /// <summary>
    ///     Restores stored values without applying constraints or recalculating the objective.
    /// </summary>
    public static OptimizationTrialResult Restore(
        OptimizationTrial trial,
        SampleMetrics? metrics,
        OptimizationTrialStatus status,
        decimal? objectiveValue,
        string? failureCode,
        long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMs);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new OptimizationTrialResult(trial, metrics, status, objectiveValue, failureCode, elapsedMs);
    }

    public static OptimizationTrialResult RejectedByTemplate(OptimizationTrial trial)
    {
        return new OptimizationTrialResult(trial, metrics: null, OptimizationTrialStatus.RejectedByTemplate, objectiveValue: null, failureCode: null, elapsedMs: 0);
    }

    public static OptimizationTrialResult Failed(OptimizationTrial trial, string failureCode, long elapsedMs = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMs);

        return new OptimizationTrialResult(trial, metrics: null, OptimizationTrialStatus.Failed, objectiveValue: null, failureCode, elapsedMs);
    }
}