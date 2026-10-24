using Dapper;

using Npgsql;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.MarketData.Repositories.Contracts;
using ProfitGraph.Infrastructure.MarketData.Repositories.Converters;
using ProfitGraph.Infrastructure.MarketData.Repositories.Queries;

namespace ProfitGraph.Infrastructure.MarketData.Repositories;

internal sealed class CandleRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : ICandleRepository
{
    public async Task Import(CandleSeries series, IReadOnlyList<Candle> candles, DateTimeOffset importedAt, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("Candle imports require an active transaction.");
        }

        var parameters = new
        {
            Venue = series.Venue.Value,
            Symbol = series.Symbol.Value,
            Timeframe = series.Timeframe.Key,
            ImportedAt = importedAt,
            OpenTimes = candles.Select(candle => candle.OpenTime).ToArray(),
            Opens = candles.Select(candle => candle.Open).ToArray(),
            Highs = candles.Select(candle => candle.High).ToArray(),
            Lows = candles.Select(candle => candle.Low).ToArray(),
            Closes = candles.Select(candle => candle.Close).ToArray(),
            Volumes = candles.Select(candle => candle.Volume).ToArray()
        };

        await connection.ExecuteAsync(new CommandDefinition(CandleQueries.Import, parameters, transactions.Current, cancellationToken: cancellationToken));
    }

    public async Task<int> Append(CandleSeries series, IReadOnlyList<Candle> candles, string source, DateTimeOffset importedAt, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("Candle downloads store each chunk in its own transaction.");
        }

        var parameters = new
        {
            Venue = series.Venue.Value,
            Symbol = series.Symbol.Value,
            Timeframe = series.Timeframe.Key,
            Source = source,
            ImportedAt = importedAt,
            OpenTimes = candles.Select(candle => candle.OpenTime).ToArray(),
            Opens = candles.Select(candle => candle.Open).ToArray(),
            Highs = candles.Select(candle => candle.High).ToArray(),
            Lows = candles.Select(candle => candle.Low).ToArray(),
            Closes = candles.Select(candle => candle.Close).ToArray(),
            Volumes = candles.Select(candle => candle.Volume).ToArray()
        };

        return await connection.ExecuteAsync(new CommandDefinition(CandleQueries.Append, parameters, transactions.Current, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Candle>> Get(CandleSeries series, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Venue = series.Venue.Value,
            Symbol = series.Symbol.Value,
            Timeframe = series.Timeframe.Key,
            From = from,
            To = to,
            Limit = limit
        };
        IEnumerable<CandleDb> rows = await connection.QueryAsync<CandleDb>(
            new CommandDefinition(CandleQueries.Get, parameters, transactions.Current, cancellationToken: cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }
}