using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Bots.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Bots.ChangeBotForeignOrders;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBot;
using ProfitGraph.Application.Bots.GetBots;
using ProfitGraph.Application.Bots.GetManualOrder;
using ProfitGraph.Application.Bots.KillBot;
using ProfitGraph.Application.Bots.StartBot;
using ProfitGraph.Application.Bots.StopBot;

namespace ProfitGraph.Api.Bots;

/// <summary>
///     The signed-in user's live bots (ADR 0016). Another user's bot answers 404, as if it did not exist. Start and stop
///     change the desired state; the Worker that holds the bot's lease follows it.
/// </summary>
[ApiController]
[Route("api/bots")]
public sealed class BotsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BotResponse>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<BotInternal> bots = await sender.Send(new GetBotsQuery(UserId()), cancellationToken);

        return bots.Select(bot => bot.ToResponse()).ToArray();
    }

    [HttpPost]
    public async Task<ActionResult<BotResponse>> Create(CreateBotRequest request, CancellationToken cancellationToken)
    {
        BotInternal bot = await sender.Send(request.ToCommand(UserId()), cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = bot.Id }, bot.ToResponse());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BotResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBotQuery(UserId(), id), cancellationToken)).ToResponse();
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<BotResponse>> Start(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new StartBotCommand(UserId(), id), cancellationToken)).ToResponse();
    }

    [HttpPost("{id:guid}/stop")]
    public async Task<ActionResult<BotResponse>> Stop(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new StopBotCommand(UserId(), id), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     The kill switch: the bot stops at once, its unsent submits are rejected and its open orders cancelled; its
    ///     position is never sold. Only an explicit start clears it.
    /// </summary>
    [HttpPost("{id:guid}/kill")]
    public async Task<ActionResult<BotResponse>> Kill(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new KillBotCommand(UserId(), id), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     An owner market order in a running bot (Profit Fox Designer ADR 0091). It waits for the bot's runner, which takes
    ///     it within a minute or lets it expire; poll it by its identifier.
    /// </summary>
    [HttpPost("{id:guid}/manual-orders")]
    public async Task<ActionResult<ManualOrderResponse>> SubmitManualOrder(Guid id, SubmitManualOrderRequest request, CancellationToken cancellationToken)
    {
        ManualOrderInternal order = await sender.Send(request.ToCommand(UserId(), id), cancellationToken);

        return AcceptedAtAction(nameof(GetManualOrder), new { id, orderId = order.Id }, order.ToResponse());
    }

    [HttpGet("{id:guid}/manual-orders/{orderId:guid}")]
    public async Task<ActionResult<ManualOrderResponse>> GetManualOrder(Guid id, Guid orderId, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetManualOrderQuery(UserId(), id, orderId), cancellationToken)).ToResponse();
    }

    [HttpPut("{id:guid}/parameters")]
    public async Task<ActionResult<BotResponse>> ChangeParameters(Guid id, ChangeBotParametersRequest request, CancellationToken cancellationToken)
    {
        return (await sender.Send(request.ToCommand(UserId(), id), cancellationToken)).ToResponse();
    }

    [HttpPut("{id:guid}/risk-limits")]
    public async Task<ActionResult<BotResponse>> ChangeRiskLimits(Guid id, ChangeBotRiskLimitsRequest request, CancellationToken cancellationToken)
    {
        return (await sender.Send(request.ToCommand(UserId(), id), cancellationToken)).ToResponse();
    }

    [HttpPut("{id:guid}/foreign-orders")]
    public async Task<ActionResult<BotResponse>> ChangeForeignOrders(Guid id, ChangeBotForeignOrdersRequest request, CancellationToken cancellationToken)
    {
        return (await sender.Send(new ChangeBotForeignOrdersCommand(UserId(), id, request.AllowForeignOrders), cancellationToken)).ToResponse();
    }

    private Guid UserId()
    {
        return AuthenticationSetup.UserId(User) ?? throw new InvalidOperationException("An authorized request has no user identifier.");
    }
}