namespace ProfitGraph.Application.Bots;

/// <summary>
///     The stable codes of an owner's order cancel (ADRs 0048 and 0055): Problem Details codes of the endpoint and rejection reasons of the
///     request.
/// </summary>
internal static class OrderCancelRequests
{
    public const string Expired = "expired";
    public const string PendingCode = "order_cancel_request_pending";
    public const string NotFoundCode = "order_cancel_not_found";

    /// <summary>
    ///     The order is not working in the bot's engine: it filled, was cancelled or rejected, or never existed.
    /// </summary>
    public const string NotOpenCode = "order_not_open";

    /// <summary>
    ///     The engine already asks the venue to cancel the order, by the strategy or by an earlier owner cancel.
    /// </summary>
    public const string AlreadyCancellingCode = "order_cancel_pending";

    /// <summary>
    ///     The order of a stopped bot never left for the venue: its submit is held until the bot starts (ADR 0055).
    /// </summary>
    public const string NotSentCode = "order_not_sent";
}