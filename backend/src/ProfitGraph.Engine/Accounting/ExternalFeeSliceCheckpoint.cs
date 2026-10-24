namespace ProfitGraph.Engine.Accounting;

public sealed record ExternalFeeSliceCheckpoint(
    decimal GrossBase,
    decimal GrossQuote,
    decimal Fee,
    decimal RemainingShares,
    bool CommissionPending);