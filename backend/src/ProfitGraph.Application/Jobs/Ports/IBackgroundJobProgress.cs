namespace ProfitGraph.Application.Jobs.Ports;

/// <summary>
///     Takes progress of the job the current work belongs to. Outside a job, for example when a command runs directly,
///     it does nothing.
/// </summary>
public interface IBackgroundJobProgress
{
    Task Report(long completed, long total, CancellationToken cancellationToken);

    /// <summary>
    ///     Starts counting the next stage of the work, such as replay after history preparation.
    /// </summary>
    Task Restart(long completed, long total, CancellationToken cancellationToken);
}