namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Итог выставления: принятый и зарезервированный ордер либо отказ. Принятие не означает исполнения.
/// </summary>
public sealed record OrderSubmission(Order? Order, OrderRejection? Rejection)
{
    public bool IsAccepted => Order is not null;
}