using Dapper;

using Npgsql;

using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories;

internal sealed class BacktestFactStore(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBacktestFactStore
{
    public async Task Append(BacktestRunId runId, BacktestFactBatch batch, CancellationToken cancellationToken)
    {
        NpgsqlTransaction transaction = transactions.Current ?? throw new InvalidOperationException("Run facts are written inside a transaction.");
        var parameters = new
        {
            RunId = runId.Value,
            Ledger = BacktestJsonSerializer.Serialize(batch.Ledger.Select(entry => entry.ToDb())),
            Candles = BacktestJsonSerializer.Serialize(batch.Candles.Select(candle => candle.ToDb())),
            Diagnostics = BacktestJsonSerializer.Serialize(batch.Diagnostics.Select(diagnostic => diagnostic.ToDb())),
            LedgerCount = (long)batch.Ledger.Count,
            CandleCount = (long)batch.Candles.Count,
            DiagnosticCount = (long)batch.Diagnostics.Count
        };

        await connection.ExecuteAsync(new CommandDefinition(BacktestFactQueries.Append, parameters, transaction, cancellationToken: cancellationToken));
    }
}