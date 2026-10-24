using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Jobs;
using ProfitGraph.Api.Jobs.Contracts;
using ProfitGraph.Api.Jobs.Converters;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Api.Backtesting;

[ApiController]
[Route("api/backtests")]
public sealed class BacktestsController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     Freezes a run of one of the user's strategies and queues it for the Worker (ADR 0012). The run has the job's
    ///     identifier; its progress and outcome are at /api/jobs/{id}.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BackgroundJobResponse>> Start(StartBacktestRequest request, CancellationToken cancellationToken)
    {
        Guid userId = AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
        BackgroundJobInternal job = await sender.Send(request.ToCommand(userId), cancellationToken);

        return AcceptedAtAction(nameof(JobsController.Get), "Jobs", new { id = job.Id }, job.ToResponse());
    }
}