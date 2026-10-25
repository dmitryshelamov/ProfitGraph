using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;

namespace ProfitGraph.Application.Accounts.EnrollCredential;

internal sealed class StartCredentialEnrollmentCommandHandler(CredentialEnrollments enrollments)
    : IRequestHandler<StartCredentialEnrollmentCommand, CredentialEnrollmentInternal>
{
    public async Task<CredentialEnrollmentInternal> Handle(StartCredentialEnrollmentCommand command, CancellationToken cancellationToken)
    {
        IVenueCredentialEnrollment venue = enrollments.Venue(command.Venue);
        var enrollmentId = Guid.NewGuid();
        CredentialEnrollmentStepInternal step = await venue.Start(command.OwnerId, enrollmentId, command.Label, command.Fields, cancellationToken);

        return await enrollments.Answer(venue, command.OwnerId, enrollmentId, step, cancellationToken);
    }
}