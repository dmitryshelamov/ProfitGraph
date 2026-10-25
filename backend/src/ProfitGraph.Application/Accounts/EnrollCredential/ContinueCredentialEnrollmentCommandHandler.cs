using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;

namespace ProfitGraph.Application.Accounts.EnrollCredential;

internal sealed class ContinueCredentialEnrollmentCommandHandler(CredentialEnrollments enrollments)
    : IRequestHandler<ContinueCredentialEnrollmentCommand, CredentialEnrollmentInternal>
{
    public async Task<CredentialEnrollmentInternal> Handle(ContinueCredentialEnrollmentCommand command, CancellationToken cancellationToken)
    {
        IVenueCredentialEnrollment venue = enrollments.Venue(command.Venue);
        CredentialEnrollmentStepInternal step = await venue.Continue(command.OwnerId, command.EnrollmentId, command.Step, command.Fields, cancellationToken);

        return await enrollments.Answer(venue, command.OwnerId, command.EnrollmentId, step, cancellationToken);
    }
}