using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     An order the bot has open; quantities and prices are exact invariant decimal strings. A market buy has a quote budget
///     instead of a quantity, a limit order a limit price.
/// </summary>
public sealed record BotOpenOrderResponse(
    long EngineOrderId,
    string? Tag,
    OrderSide Side,
    OrderType Type,
    OrderStatus Status,
    string? Quantity,
    string? LimitPrice,
    string? QuoteBudget,
    string FilledBase);
