namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     Pending enrollments, each changed only from the step a reader saw.
/// </summary>
internal interface ISteamEnrollmentStore
{
    Task Add(SteamPendingEnrollment enrollment, CancellationToken cancellationToken);

    Task<SteamPendingEnrollment?> Get(Guid id, CancellationToken cancellationToken);

    Task<bool> Transition(SteamPendingEnrollment next, string expectedStep, CancellationToken cancellationToken);

    Task Delete(Guid id, CancellationToken cancellationToken);
}