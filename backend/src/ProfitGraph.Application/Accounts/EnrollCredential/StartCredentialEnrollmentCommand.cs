using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.EnrollCredential;

/// <summary>
///     Starts creating a credential with a venue that enrolls them in steps. The fields are never stored or printed by the
///     core. It calls the venue, so it runs outside a transaction.
/// </summary>
public sealed record StartCredentialEnrollmentCommand(Guid OwnerId, string Venue, string Label, IReadOnlyDictionary<string, string> Fields)
    : ICommand<CredentialEnrollmentInternal>, INonTransactionalCommand
{
    public override string ToString()
    {
        return $"StartCredentialEnrollmentCommand {{ OwnerId = {OwnerId}, Venue = {Venue}, Redacted }}";
    }
}