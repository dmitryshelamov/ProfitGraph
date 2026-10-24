using Dapper;

using MediatR;

using Microsoft.Extensions.Options;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Common;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Common.Outbox;

/// <summary>
///     Delivers one stored event to its MediatR handlers inside one transaction that also marks it delivered. Handlers
///     only write to the database, so a crash before commit rolls back both and the event is delivered again: each event
///     takes effect once. A failed delivery waits with exponential backoff.
/// </summary>
internal sealed class OutboxDelivery(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    ITransactionExecutor executor,
    OutboxEventTypes types,
    IPublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider clock)
{
    internal const string UnknownTypeError = "unknown_event_type";

    /// <summary>
    ///     Returns false when no event is due.
    /// </summary>
    public async Task<bool> DeliverNext(CancellationToken cancellationToken)
    {
        (OutboxMessageDb Message, string Error)? failure = null;
        bool found;

        try
        {
            found = await executor.ExecuteAsync(
                async () =>
                {
                    DateTimeOffset now = clock.GetUtcNow();
                    var message = await connection.QuerySingleOrDefaultAsync<OutboxMessageDb>(Command(OutboxQueries.ClaimNext, new { now }, cancellationToken));

                    if (message is null)
                    {
                        return false;
                    }

                    IDomainEvent? domainEvent = types.Deserialize(message.Type, message.Payload);

                    if (domainEvent is null)
                    {
                        failure = (message, UnknownTypeError);

                        return true;
                    }

                    try
                    {
                        await publisher.Publish(domainEvent, cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failure = (message, exception.GetType().Name);

                        throw;
                    }

                    await connection.ExecuteAsync(Command(OutboxQueries.MarkDelivered, new { id = message.Id, now }, cancellationToken));

                    return true;
                },
                cancellationToken);
        }
        catch (Exception) when (failure is not null)
        {
            found = true;
        }

        if (failure is { } failed)
        {
            await RecordFailure(failed.Message, failed.Error, cancellationToken);
        }

        return found;
    }

    private async Task RecordFailure(OutboxMessageDb message, string error, CancellationToken cancellationToken)
    {
        double seconds = Math.Min(Math.Pow(x: 2, Math.Min(message.Attempts, val2: 30)), options.Value.MaxRetryDelay.TotalSeconds);
        DateTimeOffset nextAttemptAt = clock.GetUtcNow().AddSeconds(seconds);
        await executor.ExecuteAsync(
            () => connection.ExecuteAsync(
                Command(OutboxQueries.RecordFailure, new { id = message.Id, nextAttemptAt, error = error[..Math.Min(error.Length, val2: 200)] }, cancellationToken)),
            cancellationToken);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}