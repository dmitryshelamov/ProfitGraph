using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Bots.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Bots.AcknowledgeBotAlert;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotAlerts;

namespace ProfitGraph.Api.Bots;

/// <summary>
///     Alerts about live bots (ADR 0018), newest first. A user reads and acknowledges the alerts of their own bots; the
///     Owner may also read every user's with <c>everyUser=true</c>.
/// </summary>
[ApiController]
[Route("api")]
public sealed class BotAlertsController(ISender sender) : ControllerBase
{
    [HttpGet("alerts")]
    public async Task<ActionResult<IReadOnlyList<BotAlertResponse>>> List(
        [FromQuery] bool unacknowledgedOnly = false,
        [FromQuery] bool everyUser = false,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        bool everything = everyUser && User.IsInRole(AuthenticationSetup.OwnerRole);

        return await Read(new GetBotAlertsQuery(UserId(), everything, BotId: null, unacknowledgedOnly, limit), cancellationToken);
    }

    [HttpGet("bots/{id:guid}/alerts")]
    public async Task<ActionResult<IReadOnlyList<BotAlertResponse>>> ListOfBot(
        Guid id,
        [FromQuery] bool unacknowledgedOnly = false,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await Read(new GetBotAlertsQuery(UserId(), EveryUser: false, id, unacknowledgedOnly, limit), cancellationToken);
    }

    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<ActionResult<BotAlertResponse>> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new AcknowledgeBotAlertCommand(UserId(), id), cancellationToken)).ToResponse();
    }

    private async Task<ActionResult<IReadOnlyList<BotAlertResponse>>> Read(GetBotAlertsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<BotAlertInternal> alerts = await sender.Send(query, cancellationToken);

        return alerts.Select(alert => alert.ToResponse()).ToArray();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}