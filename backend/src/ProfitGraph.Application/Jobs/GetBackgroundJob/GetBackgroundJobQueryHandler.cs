using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs.GetBackgroundJob;

internal sealed class GetBackgroundJobQueryHandler(IBackgroundJobRepository repository) : IRequestHandler<GetBackgroundJobQuery, BackgroundJobInternal>
{
    public async Task<BackgroundJobInternal> Handle(GetBackgroundJobQuery request, CancellationToken cancellationToken)
    {
        BackgroundJob job = await repository.GetOwned(request.Id, request.UserId, cancellationToken);

        return await repository.GetDetails(job.Id, cancellationToken)
            ?? throw new ResourceNotFoundException("The background job does not exist.", BackgroundJobErrorCodes.NotFound);
    }
}