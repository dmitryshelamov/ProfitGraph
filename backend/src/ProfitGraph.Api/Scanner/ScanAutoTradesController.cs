using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Api.Scanner.Converters;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.DeleteScanAutoTradeRule;
using ProfitGraph.Application.Scanner.GetScanAutoTrade;

namespace ProfitGraph.Api.Scanner;

/// <summary>
///     Auto trades of the user's own scanner presets (ADR 0071): the rule, the backtests that would prove it, its signals and paper
///     trades. A rule is created off and trades on paper only with a proof from two backtests of opposite markets.
/// </summary>
[ApiController]
[Route("api/scanner/presets/{presetId:guid}/auto-trades")]
public sealed class ScanAutoTradesController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The preset's auto trades; the backtests' cards are shown at <c>minExpectedR</c> and <c>minChance</c>, the rule's by default.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ScanAutoTradeResponse>> Get(
        Guid presetId,
        [FromQuery] string? minExpectedR,
        [FromQuery] string? minChance,
        CancellationToken cancellationToken)
    {
        ScanAutoTradeInternal autoTrade = await sender.Send(
            new GetScanAutoTradeQuery(
                UserId(),
                presetId,
                ScanAutoTradeResponseConverter.Minimum(minExpectedR, nameof(minExpectedR)),
                ScanAutoTradeResponseConverter.Minimum(minChance, nameof(minChance))),
            cancellationToken);

        return autoTrade.ToResponse();
    }

    /// <summary>
    ///     Creates or changes the preset's rule; a changed rule is off.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<ScanAutoTradeRuleResponse>> Save(Guid presetId, ScanAutoTradeRuleRequest request, CancellationToken cancellationToken)
    {
        ScanAutoTradeRuleInternal rule = await sender.Send(request.ToCommand(UserId(), presetId), cancellationToken);

        return rule.ToResponse();
    }

    [HttpPut("mode")]
    public async Task<ActionResult<ScanAutoTradeRuleResponse>> SetMode(Guid presetId, ScanAutoTradeModeRequest request, CancellationToken cancellationToken)
    {
        ScanAutoTradeRuleInternal rule = await sender.Send(request.ToCommand(UserId(), presetId), cancellationToken);

        return rule.ToResponse();
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(Guid presetId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteScanAutoTradeRuleCommand(UserId(), presetId), cancellationToken);

        return NoContent();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}