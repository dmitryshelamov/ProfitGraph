namespace ProfitGraph.Application.Jobs.Ports;

/// <summary>
///     Takes progress of the job the current work belongs to. Outside a job, for example when a command runs directly,
///     it does nothing.
/// </summary>
public interface IBackgroundJobProgress
{
    Task Report(long completed, long total, CancellationToken cancellationToken);
}