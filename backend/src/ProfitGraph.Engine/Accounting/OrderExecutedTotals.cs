namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     What one order has executed so far: gross base traded and the net base and quote changes of the account, fees
///     included. A grid level books a buy or sale that ended after part of it filled from these (ADR 0058).
/// </summary>
public sealed record OrderExecutedTotals(decimal GrossBase, decimal BaseChange, decimal QuoteChange)
{
    public static OrderExecutedTotals None { get; } = new OrderExecutedTotals(GrossBase: 0m, BaseChange: 0m, QuoteChange: 0m);
}