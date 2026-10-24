namespace ProfitGraph.Infrastructure.Jobs.Repositories.Contracts;

/// <summary>
///     What a running job was asked to stop for; neither when it is not running.
/// </summary>
internal sealed class BackgroundJobStopRequestDb
{
    public bool CancelRequested { get; init; }
    public bool PauseRequested { get; init; }
}