using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Applies frozen actions sequentially; cancellation releases before emitting global cancellation then direct completion.
/// </summary>
public sealed class ActionExecutor
{
    public ActionApplication Apply(ActionRequest request, IGraphAccount account, EventOccurrence cause, long attemptId)
    {
        try
        {
            switch (request)
            {
                case SubmitActionRequest submit:
                    OrderSubmission result = account.Submit(submit.Order);

                    if (result.Order is { Status: OrderStatus.PendingSubmit } pending)
                    {
                        return new ActionApplication(Array.Empty<LifecycleEmission>(), pending.Id);
                    }

                    return new ActionApplication(
                        Array.AsReadOnly(
                            new[]
                            {
                                result.Order is not null
                                    ? new LifecycleEmission(EventKind.Submitted, new OrderPayload(OrderSnapshot.Capture(result.Order)))
                                    : new LifecycleEmission(EventKind.Rejected, new RejectionPayload(attemptId, result.Rejection!))
                            }));
                case CancelActionRequest cancel:
                    if (account is IExternallyExecutedAccount external)
                    {
                        ExternalCancelRequest pendingCancel = cancel.OrderId.HasValue
                            ? external.RequestCancel(cancel.OrderId.Value)
                            : external.RequestCancelByTag(cancel.Tag!);

                        return pendingCancel.Disposition switch
                        {
                            ExternalCancelDisposition.Pending => new ActionApplication(Array.Empty<LifecycleEmission>(), PendingCancelOrderId: pendingCancel.OrderId),
                            ExternalCancelDisposition.AlreadyPending => new ActionApplication(Array.Empty<LifecycleEmission>()),
                            ExternalCancelDisposition.NoTarget => new ActionApplication(
                                Array.AsReadOnly(
                                    new[]
                                    {
                                        new LifecycleEmission(
                                            EventKind.CancelCompleted,
                                            new CancelResultPayload(Cancelled: false, CancelledOrderId: null, cancel.NodeId, cause))
                                    })),
                            _ => throw new ArgumentOutOfRangeException(nameof(pendingCancel))
                        };
                    }

                    Order? order = cancel.OrderId.HasValue ? account.Cancel(cancel.OrderId.Value) : account.CancelByTag(cancel.Tag!);
                    var emissions = new List<LifecycleEmission>();

                    if (order is not null)
                    {
                        emissions.Add(new LifecycleEmission(EventKind.Cancelled, new OrderPayload(OrderSnapshot.Capture(order))));
                    }

                    emissions.Add(new LifecycleEmission(EventKind.CancelCompleted, new CancelResultPayload(order is not null, order?.Id, cancel.NodeId, cause)));

                    return new ActionApplication(emissions.AsReadOnly());
                default:
                    throw new ArgumentException("Unknown action request.", nameof(request));
            }
        }
        catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException)
        {
            throw new GraphExecutionException(request.NodeId, cause.CandleIndex, exception.Message, exception, cause.Id);
        }
    }
}