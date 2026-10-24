namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Состояние принятого ордера. Active включает ордера, ожидающие следующего открытия; Rejected означает отказ при исполнении,
///     например рыночный ордер, который после пересчёта по фактической цене оказался ниже минимумов.
/// </summary>
public enum OrderStatus
{
    Active,
    Filled,
    Cancelled,
    Rejected,
    PendingSubmit,
    PendingCancel
}