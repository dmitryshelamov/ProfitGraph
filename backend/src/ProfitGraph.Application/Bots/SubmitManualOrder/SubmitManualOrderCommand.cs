using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.SubmitManualOrder;

/// <summary>
///     The owner's order in a running bot (Profit Fox Designer ADR 0091, ADR 0027): a market order, or a limit order with
///     <see cref="LimitPrice" />; a buy is sized in quote, a sell in base. The bot's runner takes it as a root within a
///     minute or it expires; a bot has at most one pending.
/// </summary>
public sealed record SubmitManualOrderCommand(Guid OwnerId, Guid BotId, OrderSide Side, decimal Amount, decimal? LimitPrice = null)
    : ICommand<ManualOrderInternal>;