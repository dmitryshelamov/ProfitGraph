using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots.FeeCorrections;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotFeeCorrectionRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotFeeCorrectionRepository
{
    public async Task<bool> TryAdd(BotFeeCorrection correction, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(
            new CommandDefinition(BotFeeCorrectionQueries.Insert, correction.ToDb(), transactions.Current, cancellationToken: cancellationToken));

        if (inserted == 1)
        {
            transactions.RegisterDomainEventClear(correction);
        }
        else
        {
            correction.ClearDomainEvents();
        }

        return inserted == 1;
    }
}