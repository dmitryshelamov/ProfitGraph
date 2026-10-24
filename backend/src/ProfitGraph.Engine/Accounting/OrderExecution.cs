namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Итог исполнения: сделка либо отказ, после которого резерв ордера освобождён.
/// </summary>
public sealed record OrderExecution(Fill? Fill, OrderRejection? Rejection)
{
    public bool IsFilled => Fill is not null;
}