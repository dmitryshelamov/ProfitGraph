using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.EnrollCredential;

/// <summary>
///     Answers the step an enrollment waits for. The fields are never stored or printed by the core.
/// </summary>
public sealed record ContinueCredentialEnrollmentCommand(Guid OwnerId, string Venue, Guid EnrollmentId, string Step, IReadOnlyDictionary<string, string> Fields)
    : ICommand<CredentialEnrollmentInternal>, INonTransactionalCommand
{
    public override string ToString()
    {
        return $"ContinueCredentialEnrollmentCommand {{ OwnerId = {OwnerId}, Venue = {Venue}, EnrollmentId = {EnrollmentId}, Step = {Step}, Redacted }}";
    }
}