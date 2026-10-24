namespace ProfitGraph.Engine.Accounting;

public sealed record ExternalOrderProgressCheckpoint(
    long OrderId,
    bool Confirmed,
    decimal GrossBase,
    decimal GrossQuote,
    decimal Fee,
    decimal BaseChange,
    decimal QuoteChange,
    decimal RealizedPnl,
    bool LocallyAccepted = false);