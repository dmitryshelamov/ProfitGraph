using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.Accounts.Ports;

/// <summary>
///     A venue that creates its credentials in several steps with the owner, such as linking a new authenticator (ADR
///     0022). The module keeps its pending state, encrypted and short-lived; the core never stores or logs the fields a step
///     takes. When the last step succeeds the module hands back the credential's fields, the core stores the credential under
///     the enrollment's own identifier, so a repeated step cannot store it twice, and tells the module it is stored.
/// </summary>
public interface IVenueCredentialEnrollment : IVenuePort
{
    Task<CredentialEnrollmentStepInternal> Start(
        Guid ownerId,
        Guid enrollmentId,
        string label,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken);

    Task<CredentialEnrollmentStepInternal> Continue(
        Guid ownerId,
        Guid enrollmentId,
        string step,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken);

    /// <summary>
    ///     The core stored the enrollment's credential under the enrollment's identifier; the module records its own state of
    ///     it and forgets the pending enrollment.
    /// </summary>
    Task Stored(Guid ownerId, Guid enrollmentId, CancellationToken cancellationToken);
}