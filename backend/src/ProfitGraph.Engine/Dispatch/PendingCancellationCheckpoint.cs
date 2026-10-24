namespace ProfitGraph.Engine.Dispatch;

public sealed record PendingCancellationCheckpoint(long OrderId, string CancelNodeId, EventCauseCheckpoint Cause);