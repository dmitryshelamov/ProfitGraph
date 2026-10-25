namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Bought base asset that becomes sellable at <see cref="UnlockAt" /> (ADR 0022, C5).
/// </summary>
internal sealed record SettlementHoldLot(decimal Quantity, DateTimeOffset UnlockAt);