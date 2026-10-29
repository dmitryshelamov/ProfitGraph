using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Jobs.Contracts;
using ProfitGraph.Api.Jobs.Converters;
using ProfitGraph.Application.Jobs.CancelBackgroundJob;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.GetActiveJobs;
using ProfitGraph.Application.Jobs.GetBackgroundJob;

namespace ProfitGraph.Api.Jobs;

/// <summary>
///     Status and cancellation of the user's background jobs (ADR 0011). Jobs are started by the features that own them.
/// </summary>
[ApiController]
[Route("api/jobs")]
public sealed class JobsController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The user's queued, running and paused jobs of every kind with their places in the Worker's lanes.
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<ActiveJobsResponse>> Active(CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetActiveJobsQuery(UserId()), cancellationToken)).ToResponse();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BackgroundJobResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        BackgroundJobInternal job = await sender.Send(new GetBackgroundJobQuery(UserId(), id), cancellationToken);

        return job.ToResponse();
    }

    /// <summary>
    ///     Cancels a queued job, or asks the Worker to stop a running one; the job then reports Cancelled.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<BackgroundJobResponse>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        BackgroundJobInternal job = await sender.Send(new CancelBackgroundJobCommand(UserId(), id), cancellationToken);

        return Accepted(job.ToResponse());
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}