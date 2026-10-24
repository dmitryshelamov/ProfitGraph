using Npgsql;

using ProfitGraph.Domain.Common;

namespace ProfitGraph.Infrastructure.Common.Postgres;

/// <summary>
///     Holds the scoped active transaction and the aggregates whose domain events are cleared only after commit.
/// </summary>
internal sealed class PostgresTransactionAccessor
{
    private readonly List<AggregateRoot> _pendingDomainEventClears = [];

    internal NpgsqlTransaction? Current { get; set; }

    /// <summary>
    ///     The aggregates written in the current transaction; their Domain events go to the outbox before commit.
    /// </summary>
    internal IReadOnlyCollection<AggregateRoot> RegisteredAggregates => _pendingDomainEventClears;

    internal void RegisterDomainEventClear(AggregateRoot aggregate)
    {
        if (!_pendingDomainEventClears.Contains(aggregate))
        {
            _pendingDomainEventClears.Add(aggregate);
        }
    }

    internal void ClearRegisteredDomainEvents()
    {
        foreach (AggregateRoot aggregate in _pendingDomainEventClears)
        {
            aggregate.ClearDomainEvents();
        }

        _pendingDomainEventClears.Clear();
    }

    internal void DiscardRegisteredDomainEventClears()
    {
        _pendingDomainEventClears.Clear();
    }
}