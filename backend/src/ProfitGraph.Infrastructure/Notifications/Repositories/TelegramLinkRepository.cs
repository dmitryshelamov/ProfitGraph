using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;
using ProfitGraph.Infrastructure.Notifications.Repositories.Converters;
using ProfitGraph.Infrastructure.Notifications.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Notifications.Repositories;

internal sealed class TelegramLinkRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : ITelegramLinkRepository
{
    public async Task<TelegramLink?> Get(UserId userId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<TelegramLinkDb>(Command(TelegramQueries.GetLink, new { userId = userId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<TelegramLink?> GetByChatForUpdate(long chatId, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A Telegram link is locked only inside a transaction.");
        }

        var row = await connection.QuerySingleOrDefaultAsync<TelegramLinkDb>(Command(TelegramQueries.GetLinkByChatForUpdate, new { chatId }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task Save(TelegramLink link, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(TelegramQueries.SaveLink, link.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(link);
    }

    public async Task Update(TelegramLink link, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(TelegramQueries.UpdateLink, link.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(link);
    }

    public async Task<bool> Delete(UserId userId, CancellationToken cancellationToken)
    {
        return await connection.ExecuteAsync(Command(TelegramQueries.DeleteLink, new { userId = userId.Value }, cancellationToken)) > 0;
    }

    public Task SaveCode(TelegramLinkCode code, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(TelegramQueries.SaveCode, code.ToDb(), cancellationToken));
    }

    public async Task<TelegramLinkCode?> TakeCode(string codeHash, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<TelegramLinkCodeDb>(Command(TelegramQueries.TakeCode, new { codeHash }, cancellationToken));

        return row?.ToDomain();
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}