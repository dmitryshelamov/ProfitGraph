using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Converters;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs.CancelBackgroundJob;

/// <summary>
///     Cancels a queued job at once and asks the Worker to stop a running one. If the Worker claims or finishes the job
///     between the read and the conditional update, the handler reads it again and decides on the new state.
/// </summary>
internal sealed class CancelBackgroundJobCommandHandler(IBackgroundJobRepository repository, TimeProvider clock)
    : IRequestHandler<CancelBackgroundJobCommand, BackgroundJobInternal>
{
    private const int Attempts = 3;

    public async Task<BackgroundJobInternal> Handle(CancelBackgroundJobCommand request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            BackgroundJob job = await repository.GetOwned(request.Id, request.UserId, cancellationToken);

            if (job.IsFinished)
            {
                throw new StateConflictException($"The job is already {job.Status}.", BackgroundJobRuleException.Finished);
            }

            if (job.CancelRequested)
            {
                throw new StateConflictException("Cancellation of this job is already requested.", BackgroundJobRuleException.CancelAlreadyRequested);
            }

            BackgroundJobStatus expected = job.Status;
            job.RequestCancel(clock.GetUtcNow());

            if (await repository.Update(job, expected, result: null, failure: null, cancellationToken))
            {
                return job.ToInternal();
            }
        }

        throw new StateConflictException("The job changed while it was being cancelled. Try again.");
    }
}