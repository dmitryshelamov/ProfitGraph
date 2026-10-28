using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Api.Strategies.Converters;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Application.Strategies.GetStrategies;
using ProfitGraph.Application.Strategies.GetStrategy;
using ProfitGraph.Application.Strategies.GetStrategyLiveImpact;
using ProfitGraph.Application.Strategies.GetStrategyVersion;
using ProfitGraph.Application.Strategies.GetStrategyVersions;
using ProfitGraph.Application.Strategies.RenameStrategy;
using ProfitGraph.Application.Strategies.SaveStrategy;
using ProfitGraph.Application.Strategies.ValidateStrategy;

namespace ProfitGraph.Api.Strategies;

/// <summary>
///     The signed-in user's strategies. Another user's strategy answers 404, as if it did not exist.
/// </summary>
[ApiController]
[Route("api/strategies")]
public sealed class StrategiesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StrategySummaryResponse>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<StrategySummaryInternal> strategies = await sender.Send(new GetStrategiesQuery(UserId()), cancellationToken);

        return strategies.Select(strategy => strategy.ToResponse()).ToArray();
    }

    [HttpPost]
    public async Task<ActionResult<StrategyResponse>> Create(CreateStrategyRequest request, CancellationToken cancellationToken)
    {
        StrategyInternal strategy = await sender.Send(
            new CreateStrategyCommand(UserId(), request.Name, request.Definition.ToInternal(), request.Layout.ToInternal()),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = strategy.Id }, strategy.ToResponse());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StrategyResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetStrategyQuery(UserId(), id), cancellationToken)).ToResponse();
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StrategyResponse>> Save(Guid id, SaveStrategyRequest request, CancellationToken cancellationToken)
    {
        var command = new SaveStrategyCommand(
            UserId(),
            id,
            request.ExpectedVersion,
            request.Name,
            request.Definition.ToInternal(),
            request.Layout.ToInternal(),
            request.ConfirmLiveImpact);

        return (await sender.Send(command, cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     Renames the strategy without a new version, so running bots are not touched.
    /// </summary>
    [HttpPut("{id:guid}/name")]
    public async Task<ActionResult<StrategyResponse>> Rename(Guid id, RenameStrategyRequest request, CancellationToken cancellationToken)
    {
        return (await sender.Send(new RenameStrategyCommand(UserId(), id, request.Name), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     The running bots a save would switch to the new version (ADR 0019) with their values, so the editor shows which new
    ///     parameters take their default (ADR 0047); a save while any exists needs <c>confirmLiveImpact</c> and otherwise answers
    ///     409 <c>live_impact</c>.
    /// </summary>
    [HttpGet("{id:guid}/live-impact")]
    public async Task<ActionResult<IReadOnlyList<LiveImpactBotResponse>>> LiveImpact(Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveImpactBotInternal> bots = await sender.Send(new GetStrategyLiveImpactQuery(UserId(), id), cancellationToken);

        return bots.Select(bot => bot.ToResponse()).ToArray();
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<StrategyVersionSummaryResponse>>> Versions(Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<StrategyVersionSummaryInternal> versions = await sender.Send(new GetStrategyVersionsQuery(UserId(), id), cancellationToken);

        return versions.Select(version => version.ToResponse()).ToArray();
    }

    [HttpGet("{id:guid}/versions/{version:int:min(1)}")]
    public async Task<ActionResult<StrategyVersionResponse>> Version(Guid id, int version, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetStrategyVersionQuery(UserId(), id, version), cancellationToken)).ToResponse();
    }

    [HttpPost("{id:guid}/validate")]
    public async Task<ActionResult<StrategyValidationResponse>> Validate(
        Guid id,
        [FromQuery] int? version,
        CancellationToken cancellationToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        ValidateStrategyRequest? request = null)
    {
        return (await sender.Send(new ValidateStrategyQuery(UserId(), id, version, request?.Definition.ToInternal()), cancellationToken)).ToResponse();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}