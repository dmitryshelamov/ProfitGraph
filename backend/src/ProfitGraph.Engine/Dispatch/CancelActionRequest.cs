namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     A proven exclusive cancellation target created only through typed factories.
/// </summary>
public sealed record CancelActionRequest : ActionRequest
{
    public long? OrderId { get; }
    public string? Tag { get; }

    private CancelActionRequest(string nodeId, long? orderId, string? tag) : base(nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        OrderId = orderId;
        Tag = tag;
    }

    public static CancelActionRequest ByOrderId(string nodeId, long orderId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);

        return new CancelActionRequest(nodeId, orderId, tag: null);
    }

    public static CancelActionRequest ByTag(string nodeId, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        return new CancelActionRequest(nodeId, orderId: null, tag);
    }
}