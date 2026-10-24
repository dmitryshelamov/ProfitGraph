using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization;

/// <summary>
///     The stable transport names of search methods, objectives, stages and trial statuses.
/// </summary>
public static class OptimizationNames
{
    public const string Auto = "auto";

    public static IReadOnlyList<string> Methods { get; } = ["auto", "grid", "random", "coarse_to_fine", "tpe"];

    public static IReadOnlyList<string> Objectives { get; } = ["in_sample_return_percent", "in_sample_total_pnl", "in_sample_return_to_drawdown"];

    public static string Method(OptimizationSearchMethod method)
    {
        return method switch
        {
            OptimizationSearchMethod.Grid => "grid",
            OptimizationSearchMethod.Random => "random",
            OptimizationSearchMethod.CoarseToFine => "coarse_to_fine",
            OptimizationSearchMethod.Tpe => "tpe",
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    public static OptimizationSearchMethod ParseMethod(string method)
    {
        return method switch
        {
            "grid" => OptimizationSearchMethod.Grid,
            "random" => OptimizationSearchMethod.Random,
            "coarse_to_fine" => OptimizationSearchMethod.CoarseToFine,
            "tpe" => OptimizationSearchMethod.Tpe,
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    public static string Objective(OptimizationObjective objective)
    {
        return objective switch
        {
            OptimizationObjective.InSampleReturnPercent => "in_sample_return_percent",
            OptimizationObjective.InSampleTotalPnl => "in_sample_total_pnl",
            OptimizationObjective.InSampleReturnToDrawdown => "in_sample_return_to_drawdown",
            _ => throw new ArgumentOutOfRangeException(nameof(objective))
        };
    }

    public static OptimizationObjective ParseObjective(string objective)
    {
        return objective switch
        {
            "in_sample_return_percent" => OptimizationObjective.InSampleReturnPercent,
            "in_sample_total_pnl" => OptimizationObjective.InSampleTotalPnl,
            "in_sample_return_to_drawdown" => OptimizationObjective.InSampleReturnToDrawdown,
            _ => throw new ArgumentOutOfRangeException(nameof(objective))
        };
    }

    public static string? Stage(OptimizationStage? stage)
    {
        return stage switch
        {
            null => null,
            OptimizationStage.LoadingHistory => "loading_history",
            OptimizationStage.Searching => "searching",
            _ => "starting_finalists"
        };
    }

    public static OptimizationTrialStatus ParseTrialStatus(string status)
    {
        return status switch
        {
            "completed" => OptimizationTrialStatus.Completed,
            "rejected_by_constraint" => OptimizationTrialStatus.RejectedByConstraint,
            "rejected_by_template" => OptimizationTrialStatus.RejectedByTemplate,
            "failed" => OptimizationTrialStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    public static string TrialStatus(OptimizationTrialStatus status)
    {
        return status switch
        {
            OptimizationTrialStatus.Completed => "completed",
            OptimizationTrialStatus.RejectedByConstraint => "rejected_by_constraint",
            OptimizationTrialStatus.RejectedByTemplate => "rejected_by_template",
            OptimizationTrialStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}