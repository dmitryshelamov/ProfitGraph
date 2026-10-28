using Dapper;

using Npgsql;

using ProfitGraph.Domain.Notifications;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;
using ProfitGraph.Infrastructure.Notifications.Repositories.Converters;
using ProfitGraph.Infrastructure.Notifications.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Notifications.Repositories;

internal sealed class TelegramNotificationRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : ITelegramNotificationRepository
{
    public async Task Add(TelegramNotification notification, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(TelegramNotificationQueries.Insert, notification.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(notification);
    }

    public async Task<IReadOnlyList<TelegramNotification>> LockDue(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        RequireTransaction();
        IEnumerable<TelegramNotificationDb> rows =
            await connection.QueryAsync<TelegramNotificationDb>(Command(TelegramNotificationQueries.LockDue, new { now, limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<TelegramNotification?> GetForUpdate(TelegramNotificationId id, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var row = await connection.QuerySingleOrDefaultAsync<TelegramNotificationDb>(
            Command(TelegramNotificationQueries.GetForUpdate, new { id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task Update(TelegramNotification notification, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(TelegramNotificationQueries.Update, notification.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(notification);
    }

    public Task<int> DeleteFinishedBefore(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(TelegramNotificationQueries.DeleteFinishedBefore, new { cutoff }, cancellationToken));
    }

    private void RequireTransaction()
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A Telegram notification is locked only inside a transaction.");
        }
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}