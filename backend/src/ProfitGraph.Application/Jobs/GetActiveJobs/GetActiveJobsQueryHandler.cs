using MediatR;

using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Application.Jobs.GetActiveJobs;

internal sealed class GetActiveJobsQueryHandler(IActiveJobReader reader) : IRequestHandler<GetActiveJobsQuery, ActiveJobsInternal>
{
    public Task<ActiveJobsInternal> Handle(GetActiveJobsQuery request, CancellationToken cancellationToken)
    {
        return reader.ForOwner(new UserId(request.UserId), cancellationToken);
    }
}