namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     Steam refused or did not answer an enrollment step. The problem is a stable code shown to the owner, never Steam's
///     text or any value the step carried.
/// </summary>
internal sealed class SteamEnrollmentException(string problem) : Exception(problem)
{
    public string Problem { get; } = problem;
}