using ProfitGraph.Venues.Steam.Enrollment;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

internal sealed class InMemorySteamEnrollmentStore : ISteamEnrollmentStore
{
    private readonly Dictionary<Guid, SteamPendingEnrollment> _enrollments = new Dictionary<Guid, SteamPendingEnrollment>();

    public IReadOnlyList<SteamPendingEnrollment> All => [.. _enrollments.Values];

    public Task Add(SteamPendingEnrollment enrollment, CancellationToken cancellationToken)
    {
        _enrollments.Add(enrollment.Id, enrollment);

        return Task.CompletedTask;
    }

    public Task<SteamPendingEnrollment?> Get(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_enrollments.GetValueOrDefault(id));
    }

    public Task<bool> Transition(SteamPendingEnrollment next, string expectedStep, CancellationToken cancellationToken)
    {
        if (!_enrollments.TryGetValue(next.Id, out SteamPendingEnrollment? current) || current.Step != expectedStep)
        {
            return Task.FromResult(false);
        }

        _enrollments[next.Id] = next;

        return Task.FromResult(true);
    }

    public Task Delete(Guid id, CancellationToken cancellationToken)
    {
        _enrollments.Remove(id);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Replaces an enrollment as a crash would leave it.
    /// </summary>
    public void Put(SteamPendingEnrollment enrollment)
    {
        _enrollments[enrollment.Id] = enrollment;
    }
}