namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Видимая причина отказа. OrderId задан, если отказ произошёл при исполнении уже принятого ордера.
/// </summary>
public sealed record OrderRejection(
    RejectionReason Reason,
    string Message,
    OrderSide Side,
    OrderType Type,
    string? Tag,
    string OriginNodeId,
    long? OrderId);