namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Extends graph accounting with delayed exchange-like order outcomes; replay accounts do not implement it.
/// </summary>
public interface IExternallyExecutedAccount : IGraphAccount
{
    public ExternalCancelRequest RequestCancel(long orderId);
    public ExternalCancelRequest RequestCancelByTag(string tag);
    public ExternalOrderUpdateResult ApplyExternalUpdate(ExternalOrderUpdate update, long rejectionAttemptId);
}