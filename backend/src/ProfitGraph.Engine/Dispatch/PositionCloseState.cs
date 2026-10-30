namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     The owner's position close the engine is working on (ADR 0064): the request it belongs to and, once the bot's orders are
///     gone, the market order that sells the position. While it lasts the strategy's orders are rejected locally.
/// </summary>
public sealed record PositionCloseState(Guid RequestId, long? SellOrderId = null);