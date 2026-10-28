using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots.ManualOrders;

namespace ProfitGraph.Application.Bots.Converters;

internal static class ManualOrderInternalConverter
{
    /// <summary>
    ///     A request nobody took within its lifetime reads as rejected with <c>expired</c>, even before it is stored so.
    /// </summary>
    public static ManualOrderInternal ToInternal(this ManualOrderRequest request, DateTimeOffset now)
    {
        bool expired = request.IsExpired(now);

        return new ManualOrderInternal(
            request.Id.Value,
            request.BotId.Value,
            request.Side,
            request.Type,
            request.Amount,
            request.LimitPrice,
            expired ? ManualOrderStatus.Rejected : request.Status,
            request.EngineOrderId,
            expired ? ManualOrderRequests.Expired : request.RejectionReason,
            request.CreatedAt,
            expired ? request.CreatedAt + ManualOrderRequest.Lifetime : request.ResolvedAt);
    }
}