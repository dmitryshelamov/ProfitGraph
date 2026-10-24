namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Frozen action collected before any sibling mutates the account.
/// </summary>
public abstract record ActionRequest(string NodeId);