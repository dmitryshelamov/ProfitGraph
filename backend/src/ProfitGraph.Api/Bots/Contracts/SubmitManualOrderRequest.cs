using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A market order by hand: a buy's amount is quote, a sell's is base, as an exact decimal string.
/// </summary>
public sealed record SubmitManualOrderRequest(OrderSide Side, string? Amount);