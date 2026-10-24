namespace ProfitGraph.Application.Common.Messaging;

/// <summary>
///     Marks a command whose orchestration must not be wrapped in a database transaction.
///     Use for workflows containing remote calls or long-running work.
/// </summary>
public interface INonTransactionalCommand;