namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     One external order update. Fill quantities, quote value and fee are cumulative for the order.
/// </summary>
public sealed record ExternalOrderUpdate(
    long OrderId,
    ExternalOrderUpdateKind Kind,
    decimal? CumulativeGrossBase = null,
    decimal? CumulativeGrossQuote = null,
    decimal? CumulativeFee = null,
    RejectionReason? Reason = null,
    string? Message = null,
    bool OwnerConfirmedAbsent = false,
    bool CommissionPending = false,
    bool CommissionPartitioned = false)
{
    public static ExternalOrderUpdate AcceptLocally(long orderId)
    {
        return new ExternalOrderUpdate(orderId, ExternalOrderUpdateKind.LocalAccepted);
    }

    public static ExternalOrderUpdate Accept(long orderId)
    {
        return new ExternalOrderUpdate(orderId, ExternalOrderUpdateKind.Accepted);
    }

    public static ExternalOrderUpdate Reject(long orderId, RejectionReason reason, string message, bool ownerConfirmedAbsent = false)
    {
        return new ExternalOrderUpdate(orderId, ExternalOrderUpdateKind.Rejected, Reason: reason, Message: message, OwnerConfirmedAbsent: ownerConfirmedAbsent);
    }

    public static ExternalOrderUpdate Partial(
        long orderId,
        decimal grossBase,
        decimal grossQuote,
        decimal fee,
        bool commissionPending = false,
        bool commissionPartitioned = false)
    {
        return new ExternalOrderUpdate(
            orderId,
            ExternalOrderUpdateKind.PartialFill,
            grossBase,
            grossQuote,
            fee,
            CommissionPending: commissionPending,
            CommissionPartitioned: commissionPartitioned);
    }

    public static ExternalOrderUpdate Final(
        long orderId,
        decimal grossBase,
        decimal grossQuote,
        decimal fee,
        bool commissionPending = false,
        bool commissionPartitioned = false)
    {
        return new ExternalOrderUpdate(
            orderId,
            ExternalOrderUpdateKind.FinalFill,
            grossBase,
            grossQuote,
            fee,
            CommissionPending: commissionPending,
            CommissionPartitioned: commissionPartitioned);
    }

    public static ExternalOrderUpdate ConfirmCancel(long orderId)
    {
        return new ExternalOrderUpdate(orderId, ExternalOrderUpdateKind.CancelConfirmed);
    }

    public static ExternalOrderUpdate CorrectFee(
        long orderId,
        decimal grossBase,
        decimal grossQuote,
        decimal cumulativeFee,
        bool commissionPending = false,
        bool commissionPartitioned = false)
    {
        return new ExternalOrderUpdate(
            orderId,
            ExternalOrderUpdateKind.FeeCorrection,
            grossBase,
            grossQuote,
            cumulativeFee,
            CommissionPending: commissionPending,
            CommissionPartitioned: commissionPartitioned);
    }
}