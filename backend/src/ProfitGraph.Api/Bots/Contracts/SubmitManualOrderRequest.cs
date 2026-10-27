using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     An order by hand: a buy's amount is quote, a sell's is base, as an exact decimal string. A missing type is a market
///     order; a limit order needs a price and a market order takes none.
/// </summary>
public sealed record SubmitManualOrderRequest(OrderSide Side, string? Amount, OrderType? Type = null, string? LimitPrice = null);