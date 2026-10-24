using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Jobs;
using ProfitGraph.Api.Jobs.Contracts;
using ProfitGraph.Api.Jobs.Converters;
using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Api.Optimization.Converters;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Optimization.GetOptimizationRun;
using ProfitGraph.Application.Optimization.GetOptimizationTrials;
using ProfitGraph.Application.Optimization.GetStrategyOptimizations;
using ProfitGraph.Application.Optimization.PauseOptimization;
using ProfitGraph.Application.Optimization.ResumeOptimization;

namespace ProfitGraph.Api.Optimization;

[ApiController]
[Route("api/optimizations")]
public sealed class OptimizationsController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     Freezes a parameter search of one of the user's strategies and queues it for the Worker's optimization lane
    ///     (ADR 0013). The optimization has the job's identifier; its progress and outcome are at /api/jobs/{id}.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BackgroundJobResponse>> Start(StartOptimizationRequest request, CancellationToken cancellationToken)
    {
        BackgroundJobInternal job = await sender.Send(request.ToCommand(UserId()), cancellationToken);

        return AcceptedAtAction(nameof(JobsController.Get), "Jobs", new { id = job.Id }, job.ToResponse());
    }

    /// <summary>
    ///     Pauses a queued optimization at once, or stops a running one after it stores its trials; <c>409</c> when it is
    ///     finished, paused or already stopping.
    /// </summary>
    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<BackgroundJobResponse>> Pause(Guid id, CancellationToken cancellationToken)
    {
        BackgroundJobInternal job = await sender.Send(new PauseOptimizationCommand(UserId(), id), cancellationToken);

        return AcceptedAtAction(nameof(JobsController.Get), "Jobs", new { id = job.Id }, job.ToResponse());
    }

    /// <summary>
    ///     Queues a paused optimization again; it continues from its stored trials. <c>409</c> when it is not paused.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<BackgroundJobResponse>> Resume(Guid id, CancellationToken cancellationToken)
    {
        BackgroundJobInternal job = await sender.Send(new ResumeOptimizationCommand(UserId(), id), cancellationToken);

        return AcceptedAtAction(nameof(JobsController.Get), "Jobs", new { id = job.Id }, job.ToResponse());
    }

    [HttpGet("/api/strategies/{strategyId:guid}/optimizations")]
    public async Task<ActionResult<OptimizationSummaryPageResponse>> StrategyOptimizations(
        Guid strategyId,
        [FromQuery] Guid? after,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 20)
    {
        return (await sender.Send(new GetStrategyOptimizationsQuery(UserId(), strategyId, after, limit), cancellationToken)).ToResponse();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OptimizationRunResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetOptimizationRunQuery(UserId(), id), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     Stored trials after the delivery sequence <paramref name="after" />, in delivery order.
    /// </summary>
    [HttpGet("{id:guid}/trials")]
    public async Task<ActionResult<OptimizationTrialPageResponse>> Trials(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] long after = 0,
        [FromQuery] int limit = 500)
    {
        return (await sender.Send(new GetOptimizationTrialsQuery(UserId(), id, after, limit), cancellationToken)).ToResponse();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}