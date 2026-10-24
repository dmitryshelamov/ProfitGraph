using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Jobs;

/// <summary>
///     A job transition that the lifecycle forbids, such as finishing a job twice.
/// </summary>
public sealed class BackgroundJobRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidTransition = "invalid_job_transition";
    public const string InvalidProgress = "invalid_job_progress";
    public const string Finished = "job_finished";
    public const string CancelAlreadyRequested = "job_cancel_requested";
    public const string PauseAlreadyRequested = "job_pause_requested";
    public const string NotPaused = "job_not_paused";
}