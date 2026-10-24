using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Converters;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Optimization.PauseOptimization;

/// <summary>
///     If the Worker claims or finishes the job between the read and the conditional update, the handler reads it again
///     and decides on the new state.
/// </summary>
internal sealed class PauseOptimizationCommandHandler(IOptimizationReader reader, IBackgroundJobRepository jobs, TimeProvider clock)
    : IRequestHandler<PauseOptimizationCommand, BackgroundJobInternal>
{
    private const int Attempts = 3;

    public async Task<BackgroundJobInternal> Handle(PauseOptimizationCommand request, CancellationToken cancellationToken)
    {
        await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            BackgroundJob job = await jobs.GetOwned(request.Id, request.UserId, cancellationToken);
            BackgroundJobStatus expected = job.Status;

            try
            {
                job.RequestPause(clock.GetUtcNow());
            }
            catch (BackgroundJobRuleException exception)
            {
                throw new StateConflictException(exception.Message, exception.Code);
            }

            if (await jobs.Update(job, expected, result: null, failure: null, cancellationToken))
            {
                return job.ToInternal();
            }
        }

        throw new StateConflictException("The optimization changed while it was being paused. Try again.");
    }
}