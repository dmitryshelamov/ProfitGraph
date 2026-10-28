using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots.OrderCancels;

namespace ProfitGraph.Application.Bots.Converters;

internal static class OrderCancelInternalConverter
{
    /// <summary>
    ///     A request nobody took within its lifetime reads as rejected with <c>expired</c>, even before it is stored so.
    /// </summary>
    public static OrderCancelInternal ToInternal(this OrderCancelRequest request, DateTimeOffset now)
    {
        bool expired = request.IsExpired(now);

        return new OrderCancelInternal(
            request.Id.Value,
            request.BotId.Value,
            request.EngineOrderId,
            expired ? OrderCancelStatus.Rejected : request.Status,
            expired ? OrderCancelRequests.Expired : request.RejectionReason,
            request.CreatedAt,
            expired ? request.CreatedAt + OrderCancelRequest.Lifetime : request.ResolvedAt);
    }
}