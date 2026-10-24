using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Converters;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Optimization.ResumeOptimization;

internal sealed class ResumeOptimizationCommandHandler(IOptimizationReader reader, IBackgroundJobRepository jobs, TimeProvider clock)
    : IRequestHandler<ResumeOptimizationCommand, BackgroundJobInternal>
{
    public async Task<BackgroundJobInternal> Handle(ResumeOptimizationCommand request, CancellationToken cancellationToken)
    {
        await reader.EnsureOwned(request.Id, request.UserId, cancellationToken);
        BackgroundJob job = await jobs.GetOwned(request.Id, request.UserId, cancellationToken);

        try
        {
            job.Resume(clock.GetUtcNow());
        }
        catch (BackgroundJobRuleException exception)
        {
            throw new StateConflictException(exception.Message, exception.Code);
        }

        return await jobs.Update(job, BackgroundJobStatus.Paused, result: null, failure: null, cancellationToken)
            ? job.ToInternal()
            : throw new StateConflictException("The optimization changed while it was being resumed. Try again.", BackgroundJobRuleException.NotPaused);
    }
}