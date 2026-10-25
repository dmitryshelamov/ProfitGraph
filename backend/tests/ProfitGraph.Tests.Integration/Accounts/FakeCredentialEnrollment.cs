using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Accounts;

/// <summary>
///     The test venue enrolls a credential in one step: a start, then <c>confirm</c> with the word <c>yes</c>. It completes on
///     every confirm, as a venue answering a repeated step does, and counts how often the core reported the credential stored.
/// </summary>
public sealed class FakeCredentialEnrollment : IVenueCredentialEnrollment
{
    public int Stores { get; private set; }
    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public Task<CredentialEnrollmentStepInternal> Start(
        Guid ownerId,
        Guid enrollmentId,
        string label,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new CredentialEnrollmentStepInternal("confirm", ["answer"], "shown-once"));
    }

    public Task<CredentialEnrollmentStepInternal> Continue(
        Guid ownerId,
        Guid enrollmentId,
        string step,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            fields.GetValueOrDefault("answer") == "yes"
                ? new CredentialEnrollmentStepInternal(
                    CredentialEnrollmentStepInternal.CompletedStep,
                    [],
                    Completed: new EnrolledCredentialInternal(
                        "enrolled",
                        "9876",
                        new Dictionary<string, string> { ["apiKey"] = "enrolled-key-not-real", ["apiSecret"] = "enrolled-secret-not-real" }))
                : new CredentialEnrollmentStepInternal("confirm", ["answer"], Problem: "invalid_answer"));
    }

    public Task Stored(Guid ownerId, Guid enrollmentId, CancellationToken cancellationToken)
    {
        Stores++;

        return Task.CompletedTask;
    }
}