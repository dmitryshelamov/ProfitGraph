using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Common.Outbox.PruneOutbox;

/// <summary>
///     Deletes one batch of messages delivered longer than the retention ago (ADR 0045); returns how many.
/// </summary>
public sealed record PruneOutboxCommand(TimeSpan Retention, int BatchSize) : ICommand<int>;