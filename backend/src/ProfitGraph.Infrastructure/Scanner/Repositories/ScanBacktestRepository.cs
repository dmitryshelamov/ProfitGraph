using Dapper;

using Npgsql;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanBacktestRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanBacktestRepository, IScanBacktestReader
{
    private const int CardsPerInsert = 10_000;

    public async Task<IReadOnlyList<ScanBacktestListItemInternal>> List(Guid ownerId, string venue, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<ScanBacktestListItemDb> rows = await connection.QueryAsync<ScanBacktestListItemDb>(
            Command(ScanBacktestQueries.List, new { OwnerId = ownerId, Venue = venue, Limit = limit }, cancellationToken));

        return [.. rows.Select(row => row.ToInternal())];
    }

    public async Task<ScanCardOddsSourceInternal?> LatestCardOdds(Guid ownerId, string venue, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanCardOddsSourceDb>(
            Command(ScanBacktestQueries.LatestCardOdds, new { OwnerId = ownerId, Venue = venue }, cancellationToken));

        return row?.ToInternal();
    }

    public async Task<IReadOnlyList<ScanBacktestEvidenceInternal>> Evidence(Guid ownerId, string venue, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<ScanBacktestEvidenceDb> rows = await connection.QueryAsync<ScanBacktestEvidenceDb>(
            Command(ScanBacktestQueries.Evidence, new { OwnerId = ownerId, Venue = venue, Limit = limit }, cancellationToken));

        return [.. rows.Select(row => row.ToInternal())];
    }

    public async Task<IReadOnlyList<ScanCardGroupInternal>> CardGroups(
        Guid backtestId,
        decimal minExpectedR,
        decimal? minChance,
        CancellationToken cancellationToken)
    {
        IEnumerable<ScanCardGroupDb> rows = await connection.QueryAsync<ScanCardGroupDb>(
            Command(ScanBacktestQueries.CardGroups, new { BacktestId = backtestId, MinExpectedR = minExpectedR, MinChance = minChance }, cancellationToken));

        return [.. rows.Select(row => row.ToInternal())];
    }

    public async Task AddCards(ScanBacktestId id, IReadOnlyList<ScanBacktestCard> cards, CancellationToken cancellationToken)
    {
        foreach (ScanBacktestCard[] chunk in cards.Chunk(CardsPerInsert))
        {
            await connection.ExecuteAsync(
                Command(
                    ScanBacktestQueries.AddCards,
                    new
                    {
                        BacktestId = id.Value,
                        Timeframes = chunk.Select(card => card.Timeframe).ToArray(),
                        Scenarios = chunk.Select(card => card.Scenario.ToString()).ToArray(),
                        Directions = chunk.Select(card => card.Direction.ToString()).ToArray(),
                        SignalAts = chunk.Select(card => card.SignalAt).ToArray(),
                        Thirds = chunk.Select(card => (short)card.Third).ToArray(),
                        Results = chunk.Select(card => card.ResultR).ToArray(),
                        Chances = chunk.Select(card => card.Chance).ToArray(),
                        ExpectedRs = chunk.Select(card => card.ExpectedR).ToArray()
                    },
                    cancellationToken));
        }
    }

    public async Task<ScanBacktest?> Get(ScanBacktestId id, UserId ownerId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanBacktestDb>(
            Command(ScanBacktestQueries.Get, new { Id = id.Value, OwnerId = ownerId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<ScanBacktest?> GetForUpdate(ScanBacktestId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanBacktestDb>(Command(ScanBacktestQueries.GetForUpdate, new { Id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public Task Add(ScanBacktest backtest, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanBacktestQueries.Add, Parameters(backtest), cancellationToken));
    }

    public Task Update(ScanBacktest backtest, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanBacktestQueries.Update, Parameters(backtest), cancellationToken));
    }

    private static object Parameters(ScanBacktest backtest)
    {
        return new
        {
            Id = backtest.Id.Value,
            OwnerId = backtest.OwnerId.Value,
            Venue = backtest.Venue.Value,
            PresetName = backtest.PresetName.Value,
            FilterJson = backtest.ToFilterJson(),
            PeriodFrom = backtest.From,
            PeriodTo = backtest.To,
            JobId = backtest.JobId.Value,
            backtest.CreatedAt,
            backtest.CompletedAt,
            ResultJson = backtest.ToResultJson(),
            CardOddsJson = backtest.ToCardOddsJson(),
            backtest.Result?.MarketChange
        };
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}