using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Accounts.EnrollCredential;

/// <summary>
///     Runs a venue's enrollment steps and stores the credential the last one produces (ADR 0022): encrypted like every
///     credential, under the enrollment's identifier, so a step that is answered again finds it stored and only tells the
///     venue once more.
/// </summary>
public sealed class CredentialEnrollments(
    VenuePorts<IVenueCredentialEnrollment> venues,
    IExchangeCredentialRepository credentials,
    ISecretProtector protector,
    ITransactionExecutor transactions,
    TimeProvider clock)
{
    public IVenueCredentialEnrollment Venue(string venue)
    {
        protector.EnsureAvailable();

        return venues.Find(new VenueId(venue)) ??
            throw new ResourceNotFoundException("The venue does not enroll credentials.", ExchangeCredentialErrorCodes.EnrollmentUnsupported);
    }

    public async Task<CredentialEnrollmentInternal> Answer(
        IVenueCredentialEnrollment venue,
        Guid ownerId,
        Guid enrollmentId,
        CredentialEnrollmentStepInternal step,
        CancellationToken cancellationToken)
    {
        if (step.Completed is not { } completed)
        {
            return new CredentialEnrollmentInternal(enrollmentId, venue.Venue.Value, step.Step, step.Fields, step.OneTimeSecret, step.Problem, CredentialId: null);
        }

        var id = new ExchangeCredentialId(enrollmentId);
        var owner = new UserId(ownerId);

        if (await credentials.Get(id, cancellationToken) is null)
        {
            var credential = ExchangeCredential.Create(
                id,
                owner,
                venue.Venue,
                new ExchangeCredentialLabel(completed.Label),
                new ApiKeyLast4(completed.PublicHint),
                completed.Fields.ToDictionary(field => field.Key, field => protector.Encrypt(id, owner, field.Key, field.Value), StringComparer.Ordinal),
                clock.GetUtcNow(),
                publicId: completed.PublicId is null ? null : new CredentialPublicId(completed.PublicId));
            await transactions.ExecuteAsync(
                async () =>
                {
                    await credentials.Add(credential, cancellationToken);

                    return true;
                },
                cancellationToken);
        }

        await venue.Stored(ownerId, enrollmentId, cancellationToken);

        return new CredentialEnrollmentInternal(
            enrollmentId,
            venue.Venue.Value,
            CredentialEnrollmentStepInternal.CompletedStep,
            [],
            step.OneTimeSecret,
            step.Problem,
            enrollmentId);
    }
}