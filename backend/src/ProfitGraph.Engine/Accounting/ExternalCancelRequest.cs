namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     A cancellation request is either a completed no-op, newly pending, or an idempotent repeat.
/// </summary>
public sealed record ExternalCancelRequest(ExternalCancelDisposition Disposition, long? OrderId);