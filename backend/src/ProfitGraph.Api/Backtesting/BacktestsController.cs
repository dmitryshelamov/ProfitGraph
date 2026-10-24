using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Jobs;
using ProfitGraph.Api.Jobs.Contracts;
using ProfitGraph.Api.Jobs.Converters;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.GetBacktestChart;
using ProfitGraph.Application.Backtesting.GetBacktestDiagnostics;
using ProfitGraph.Application.Backtesting.GetBacktestFills;
using ProfitGraph.Application.Backtesting.GetBacktestObservations;
using ProfitGraph.Application.Backtesting.GetBacktestOrders;
using ProfitGraph.Application.Backtesting.GetBacktestRanking;
using ProfitGraph.Application.Backtesting.GetBacktestRun;
using ProfitGraph.Application.Backtesting.GetStrategyBacktests;
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
        BackgroundJobInternal job = await sender.Send(request.ToCommand(UserId()), cancellationToken);

        return AcceptedAtAction(nameof(JobsController.Get), "Jobs", new { id = job.Id }, job.ToResponse());
    }

    /// <summary>
    ///     The user's completed runs in one quote currency, best full-period return first (stage 3 plan, S3-4).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<BacktestSummaryPageResponse>> Ranking(
        [FromQuery] string? quote,
        [FromQuery] string? symbol,
        [FromQuery] string? timeframe,
        [FromQuery] Guid? after,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 50)
    {
        BacktestSummaryPageInternal page = await sender.Send(
            new GetBacktestRankingQuery(UserId(), quote ?? string.Empty, symbol, timeframe, after, limit),
            cancellationToken);

        return page.ToResponse();
    }

    [HttpGet("/api/strategies/{strategyId:guid}/backtests")]
    public async Task<ActionResult<BacktestSummaryPageResponse>> StrategyRuns(
        Guid strategyId,
        [FromQuery] Guid? after,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 20)
    {
        return (await sender.Send(new GetStrategyBacktestsQuery(UserId(), strategyId, after, limit), cancellationToken)).ToResponse();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BacktestRunResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBacktestRunQuery(UserId(), id), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     Candle observations after <paramref name="after" />, with the facts reported with each; while a run is replaying
    ///     this is how a client fills gaps in progress messages. A finished run keeps only its last observation.
    /// </summary>
    [HttpGet("{id:guid}/observations")]
    public async Task<ActionResult<BacktestPageResponse<BacktestObservationResponse>>> Observations(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] long after = 0,
        [FromQuery] int limit = 100)
    {
        BacktestPageInternal<BacktestObservationInternal> page = await sender.Send(new GetBacktestObservationsQuery(UserId(), id, after, limit), cancellationToken);

        return page.ToResponse(observation => observation.ToResponse());
    }

    /// <summary>
    ///     The latest state of each order, by order identifier after <paramref name="after" />.
    /// </summary>
    [HttpGet("{id:guid}/orders")]
    public async Task<ActionResult<BacktestPageResponse<BacktestLedgerEntryResponse>>> Orders(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] long after = 0,
        [FromQuery] int limit = 100)
    {
        BacktestPageInternal<BacktestLedgerEntryInternal> page = await sender.Send(new GetBacktestOrdersQuery(UserId(), id, after, limit), cancellationToken);

        return page.ToResponse(entry => entry.ToResponse());
    }

    [HttpGet("{id:guid}/fills")]
    public async Task<ActionResult<BacktestPageResponse<BacktestLedgerEntryResponse>>> Fills(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] long after = 0,
        [FromQuery] int limit = 100)
    {
        BacktestPageInternal<BacktestLedgerEntryInternal> page = await sender.Send(new GetBacktestFillsQuery(UserId(), id, after, limit), cancellationToken);

        return page.ToResponse(entry => entry.ToResponse());
    }

    [HttpGet("{id:guid}/diagnostics")]
    public async Task<ActionResult<BacktestPageResponse<BacktestDiagnosticResponse>>> Diagnostics(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] long after = 0,
        [FromQuery] int limit = 100)
    {
        BacktestPageInternal<BacktestDiagnosticInternal> page = await sender.Send(new GetBacktestDiagnosticsQuery(UserId(), id, after, limit), cancellationToken);

        return page.ToResponse(diagnostic => diagnostic.ToResponse());
    }

    [HttpGet("{id:guid}/chart")]
    public async Task<ActionResult<BacktestChartResponse>> Chart(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBacktestChartQuery(UserId(), id), cancellationToken)).ToResponse();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}