using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Bots.Converters;
using ProfitGraph.Api.Common.Authentication;
using ProfitGraph.Application.Bots.CancelBotOrder;
using ProfitGraph.Application.Bots.ChangeBotForeignOrders;
using ProfitGraph.Application.Bots.ConfirmOrderAbsent;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBot;
using ProfitGraph.Application.Bots.GetBotAccount;
using ProfitGraph.Application.Bots.GetBotCandles;
using ProfitGraph.Application.Bots.GetBotFeed;
using ProfitGraph.Application.Bots.GetBotFills;
using ProfitGraph.Application.Bots.GetBotOpenOrders;
using ProfitGraph.Application.Bots.GetBotPosition;
using ProfitGraph.Application.Bots.GetBots;
using ProfitGraph.Application.Bots.GetBotUnresolvedOrders;
using ProfitGraph.Application.Bots.GetBotWaitingOrders;
using ProfitGraph.Application.Bots.GetManualOrder;
using ProfitGraph.Application.Bots.GetOrderCancel;
using ProfitGraph.Application.Bots.KillBot;
using ProfitGraph.Application.Bots.RenameBot;
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
    ///     An owner order, market or limit, in a running bot (Profit Fox Designer ADR 0091, ADR 0046). It waits for the bot's runner, which takes
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

    /// <summary>
    ///     A page of the bot's chart: its closed candles of the half-open UTC range [from, to), at most 1000 of them, with its
    ///     fills of the same range. The venue, symbol, timeframe and market data access are the bot's own (ADR 0026).
    /// </summary>
    [HttpGet("{id:guid}/candles")]
    public async Task<ActionResult<BotCandlesResponse>> Candles(
        Guid id,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBotCandlesQuery(UserId(), id, from, to), cancellationToken)).ToResponse();
    }

    [HttpGet("{id:guid}/fills")]
    public async Task<ActionResult<BotFillPageResponse>> Fills(
        Guid id,
        [FromQuery] long? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return (await sender.Send(new GetBotFillsQuery(UserId(), id, before, limit), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     The bot's working orders that its venue holds locally, with why (ADR 0023): a price band, a closed session, a carry-over
    ///     to the next session, a settlement hold and the like.
    /// </summary>
    [HttpGet("{id:guid}/waiting-orders")]
    public async Task<ActionResult<IReadOnlyList<BotOrderWaitingResponse>>> WaitingOrders(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBotWaitingOrdersQuery(UserId(), id), cancellationToken)).Select(waiting => waiting.ToResponse()).ToArray();
    }

    /// <summary>
    ///     The bot's open orders as its engine holds them: limit exits and averaging orders waiting at the venue, orders about to be
    ///     submitted and orders about to be cancelled.
    /// </summary>
    [HttpGet("{id:guid}/open-orders")]
    public async Task<ActionResult<IReadOnlyList<BotOpenOrderResponse>>> OpenOrders(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBotOpenOrdersQuery(UserId(), id), cancellationToken)).Select(order => order.ToResponse()).ToArray();
    }

    /// <summary>
    ///     The owner cancels one of the bot's open orders. A running bot's request waits for its runner, which turns it into the
    ///     engine's ordinary cancel within a minute or lets it expire; poll it by its identifier (ADR 0048). A stopped bot's request is
    ///     requested at once and its cancel goes to the venue (ADR 0055). A starting or stopping bot, an order that is no longer open,
    ///     an order already being cancelled and a stopped bot's order that was never sent answer 409.
    /// </summary>
    [HttpPost("{id:guid}/open-orders/{engineOrderId:long}/cancel")]
    public async Task<ActionResult<OrderCancelResponse>> CancelOrder(Guid id, long engineOrderId, CancellationToken cancellationToken)
    {
        OrderCancelInternal cancel = await sender.Send(new CancelBotOrderCommand(UserId(), id, engineOrderId), cancellationToken);

        return AcceptedAtAction(nameof(GetOrderCancel), new { id, cancelId = cancel.Id }, cancel.ToResponse());
    }

    [HttpGet("{id:guid}/order-cancels/{cancelId:guid}")]
    public async Task<ActionResult<OrderCancelResponse>> GetOrderCancel(Guid id, Guid cancelId, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetOrderCancelQuery(UserId(), id, cancelId), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     The bot's submits whose delivery to the venue is unknown, and whether each may be confirmed absent now.
    /// </summary>
    [HttpGet("{id:guid}/unresolved-orders")]
    public async Task<ActionResult<IReadOnlyList<BotUnresolvedOrderResponse>>> UnresolvedOrders(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBotUnresolvedOrdersQuery(UserId(), id), cancellationToken)).Select(order => order.ToResponse()).ToArray();
    }

    /// <summary>
    ///     The owner confirms, after checking the account on the venue, that a submit whose delivery stopped the bot never
    ///     reached it (Profit Fox Designer ADR 0055). Starting the bot again reconciles it: the venue is looked up first, and
    ///     the order is rejected only when the venue shows nothing of it.
    /// </summary>
    [HttpPost("{id:guid}/order-intents/{intentId:guid}/confirm-absent")]
    public async Task<IActionResult> ConfirmOrderAbsent(Guid id, Guid intentId, CancellationToken cancellationToken)
    {
        await sender.Send(new ConfirmOrderAbsentCommand(UserId(), id, intentId), cancellationToken);

        return NoContent();
    }

    [HttpGet("{id:guid}/feed")]
    public async Task<ActionResult<BotFeedPageResponse>> Feed(
        Guid id,
        [FromQuery] long? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return (await sender.Send(new GetBotFeedQuery(UserId(), id, before, limit), cancellationToken)).ToResponse();
    }

    [HttpGet("{id:guid}/position")]
    public async Task<ActionResult<BotPositionResponse>> Position(Guid id, [FromQuery] int cycles = 20, CancellationToken cancellationToken = default)
    {
        return (await sender.Send(new GetBotPositionQuery(UserId(), id, cycles), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     The exchange account the bot trades on, from stored data only (ADR 0049): no request reaches the venue and no
    ///     secret leaves the server.
    /// </summary>
    [HttpGet("{id:guid}/account")]
    public async Task<ActionResult<BotAccountResponse>> Account(Guid id, CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBotAccountQuery(UserId(), id), cancellationToken)).ToResponse();
    }

    [HttpPut("{id:guid}/parameters")]
    public async Task<ActionResult<BotResponse>> ChangeParameters(Guid id, ChangeBotParametersRequest request, CancellationToken cancellationToken)
    {
        return (await sender.Send(request.ToCommand(UserId(), id), cancellationToken)).ToResponse();
    }

    /// <summary>
    ///     Names the bot in any state; it changes neither trading nor the desired state.
    /// </summary>
    [HttpPut("{id:guid}/name")]
    public async Task<ActionResult<BotResponse>> Rename(Guid id, RenameBotRequest request, CancellationToken cancellationToken)
    {
        return (await sender.Send(new RenameBotCommand(UserId(), id, request.Name), cancellationToken)).ToResponse();
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