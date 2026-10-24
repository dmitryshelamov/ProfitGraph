using Dapper;

using Npgsql;

using ProfitGraph.Domain.Common;

namespace ProfitGraph.Infrastructure.Common.Outbox;

/// <summary>
///     Stores the Domain events of the aggregates a transaction wrote, inside that transaction (ADR 0004), so an event
///     exists exactly when the state change that raised it was committed.
/// </summary>
internal sealed class OutboxWriter(NpgsqlConnection connection, OutboxEventTypes types, TimeProvider clock)
{
    public async Task Write(IReadOnlyCollection<AggregateRoot> aggregates, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        foreach (IDomainEvent domainEvent in aggregates.SelectMany(aggregate => aggregate.DomainEvents))
        {
            var message = new OutboxMessageDb
            {
                Id = Guid.NewGuid(),
                Type = types.NameOf(domainEvent),
                Payload = types.Serialize(domainEvent),
                CreatedAt = now
            };
            await connection.ExecuteAsync(new CommandDefinition(OutboxQueries.Insert, message, transaction, cancellationToken: cancellationToken));
        }
    }
}