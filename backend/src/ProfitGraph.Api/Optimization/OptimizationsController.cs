using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Jobs;
using ProfitGraph.Api.Jobs.Contracts;
using ProfitGraph.Api.Jobs.Converters;
using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Api.Optimization.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Optimization;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.GetOptimizationRun;
using ProfitGraph.Application.Optimization.GetOptimizations;
using ProfitGraph.Application.Optimization.GetOptimizationTrial;
using ProfitGraph.Application.Optimization.GetOptimizationTrials;
using ProfitGraph.Application.Optimization.GetOptimizationWindows;
using ProfitGraph.Application.Optimization.GetStrategyOptimizations;
using ProfitGraph.Application.Optimization.PauseOptimization;
using ProfitGraph.Application.Optimization.ResumeOptimization;
using ProfitGraph.Domain.Optimization;

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

    /// <summary>
    ///     The user's optimizations of every strategy, newest first. <paramref name="state" /> is <c>active</c> (queued,
    ///     running or paused), <c>completed</c>, <c>failed</c> or <c>cancelled</c>; <paramref name="method" /> is a resolved
    ///     search method; <paramref name="search" /> matches a part of the symbol or the strategy name.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OptimizationSummaryPageResponse>> List(
        [FromQuery] Guid? after,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 50,
        [FromQuery] string? state = null,
        [FromQuery] Guid? strategyId = null,
        [FromQuery] string? venue = null,
        [FromQuery] string? search = null,
        [FromQuery] string? timeframe = null,
        [FromQuery] string? method = null)
    {
        var filter = new OptimizationListFilterInternal(ListState(Blank(state)), strategyId, Blank(venue), Blank(search), Blank(timeframe), Method(Blank(method)));

        return (await sender.Send(new GetOptimizationsQuery(UserId(), filter, after, limit), cancellationToken)).ToResponse();
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
    ///     The finished walk-forward windows, in order: ranges, winner, train and test metrics, efficiency and bounded test
    ///     equity.
    /// </summary>
    [HttpGet("{id:guid}/windows")]
    public async Task<ActionResult<IReadOnlyList<OptimizationWindowResponse>>> Windows(Guid id, CancellationToken cancellationToken)
    {
        OptimizationWindowsInternal windows = await sender.Send(new GetOptimizationWindowsQuery(UserId(), id), cancellationToken);

        return windows.Windows.Select(window => window.ToResponse()).ToArray();
    }

    /// <summary>
    ///     One stored trial by its index in the search; <paramref name="window" /> picks the walk-forward window.
    /// </summary>
    [HttpGet("{id:guid}/trials/{trialIndex:int}")]
    public async Task<ActionResult<OptimizationTrialResponse>> Trial(Guid id, int trialIndex, CancellationToken cancellationToken, [FromQuery] int window = 0)
    {
        return (await sender.Send(new GetOptimizationTrialQuery(UserId(), id, window, trialIndex), cancellationToken)).ToResponse();
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

    /// <summary>
    ///     An empty filter value filters nothing.
    /// </summary>
    private static string? Blank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static OptimizationListState? ListState(string? state)
    {
        return state is null
            ? null
            : OptimizationNames.ParseListState(state)
            ?? throw new ApplicationValidationException([new InputError("invalid_state", "State", "State active, completed, failed or cancelled.")]);
    }

    /// <summary>
    ///     A stored optimization has a resolved search method, so <c>auto</c> matches none.
    /// </summary>
    private static OptimizationSearchMethod? Method(string? method)
    {
        if (method is null)
        {
            return null;
        }

        return method is not OptimizationNames.Auto && OptimizationNames.Methods.Contains(method)
            ? OptimizationNames.ParseMethod(method)
            : throw new ApplicationValidationException([new InputError("invalid_method", "Method", "Method grid, random, coarse_to_fine or tpe.")]);
    }
}