namespace ProfitGraph.Engine.Accounting;

internal sealed class ExternalOrderProgress
{
    public bool LocallyAccepted { get; set; }
    public bool Confirmed { get; set; }
    public decimal GrossBase { get; set; }
    public decimal GrossQuote { get; set; }
    public decimal Fee { get; set; }
    public decimal BaseChange { get; set; }
    public decimal QuoteChange { get; set; }
    public decimal RealizedPnl { get; set; }

    internal ExternalOrderProgressCheckpoint Capture(long orderId)
    {
        return new ExternalOrderProgressCheckpoint(orderId, Confirmed, GrossBase, GrossQuote, Fee, BaseChange, QuoteChange, RealizedPnl, LocallyAccepted);
    }

    internal static ExternalOrderProgress Restore(ExternalOrderProgressCheckpoint checkpoint)
    {
        return new ExternalOrderProgress
        {
            Confirmed = checkpoint.Confirmed,
            LocallyAccepted = checkpoint.LocallyAccepted,
            GrossBase = checkpoint.GrossBase,
            GrossQuote = checkpoint.GrossQuote,
            Fee = checkpoint.Fee,
            BaseChange = checkpoint.BaseChange,
            QuoteChange = checkpoint.QuoteChange,
            RealizedPnl = checkpoint.RealizedPnl
        };
    }
}