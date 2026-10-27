namespace ProfitGraph.Application.Common.Outbox;

/// <summary>
///     The stored outbox messages, as far as use cases maintain them; storing and delivering stay in Infrastructure.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    ///     Deletes at most <paramref name="limit" /> messages delivered before <paramref name="before" />; returns how many.
    /// </summary>
    Task<int> DeleteDelivered(DateTimeOffset before, int limit, CancellationToken cancellationToken);
}