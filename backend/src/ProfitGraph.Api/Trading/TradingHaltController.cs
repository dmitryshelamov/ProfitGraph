using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Api.Trading.Contracts;
using ProfitGraph.Api.Trading.Converters;
using ProfitGraph.Application.Trading.GetTradingHalt;
using ProfitGraph.Application.Trading.HaltTrading;
using ProfitGraph.Application.Trading.ResumeTrading;

namespace ProfitGraph.Api.Trading;

/// <summary>
///     The global halt (Profit Fox Designer ADR 0056). Every user may see it; only the Owner halts, which kills every bot
///     that should run, or resumes, which starts none.
/// </summary>
[ApiController]
[Route("api/trading-halt")]
public sealed class TradingHaltController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TradingHaltResponse>> Get(CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetTradingHaltQuery(), cancellationToken)).ToResponse();
    }

    [HttpPost("halt")]
    [Authorize(Roles = AuthenticationSetup.OwnerRole)]
    public async Task<ActionResult<TradingHaltResponse>> Halt(CancellationToken cancellationToken)
    {
        return (await sender.Send(new HaltTradingCommand(), cancellationToken)).ToResponse();
    }

    [HttpPost("resume")]
    [Authorize(Roles = AuthenticationSetup.OwnerRole)]
    public async Task<ActionResult<TradingHaltResponse>> Resume(CancellationToken cancellationToken)
    {
        return (await sender.Send(new ResumeTradingCommand(), cancellationToken)).ToResponse();
    }
}