using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Persistence.Repositories.Contracts;
using ProfitGraph.Venues.TInvest.Persistence.Repositories.Queries;
using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Venues.TInvest.Persistence.Repositories;

internal sealed class TInvestOrderAttemptRepository(NpgsqlDataSource dataSource) : ITInvestOrderAttempts
{
    public async Task<bool> Add(TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(TInvestOrderAttemptQueries.Add, ToDb(attempt), cancellationToken: cancellationToken)) == 1;
    }

    public async Task<TInvestOrderAttempt?> Latest(Guid botId, long engineOrderId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TInvestOrderAttemptDb>(
            new CommandDefinition(TInvestOrderAttemptQueries.Latest, new { botId, engineOrderId }, cancellationToken: cancellationToken));

        return row is null ? null : ToAttempt(row);
    }

    public async Task<TInvestOrderAttempt?> ByRequestId(Guid requestId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TInvestOrderAttemptDb>(
            new CommandDefinition(TInvestOrderAttemptQueries.ByRequestId, new { requestId }, cancellationToken: cancellationToken));

        return row is null ? null : ToAttempt(row);
    }

    public async Task<bool> Transition(TInvestOrderAttempt next, TInvestOrderAttemptState expected, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            new CommandDefinition(
                TInvestOrderAttemptQueries.Transition,
                new
                {
                    next.RequestId,
                    State = next.State.ToString(),
                    next.ExchangeOrderId,
                    next.BrokerCode,
                    next.Price,
                    WaitingCode = next.WaitingCode?.ToString(),
                    next.BandLow,
                    next.BandHigh,
                    next.UpdatedAt,
                    Expected = expected.ToString()
                },
                cancellationToken: cancellationToken)) == 1;
    }

    public async Task<IReadOnlyList<TInvestOrderAttempt>> Waiting(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<TInvestOrderAttemptDb> rows = await connection.QueryAsync<TInvestOrderAttemptDb>(
            new CommandDefinition(TInvestOrderAttemptQueries.Waiting, cancellationToken: cancellationToken));

        return [.. rows.Select(ToAttempt)];
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> Owners(IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken)
    {
        if (requestIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<(Guid RequestId, Guid BotId)> rows = await connection.QueryAsync<(Guid RequestId, Guid BotId)>(
            new CommandDefinition(TInvestOrderAttemptQueries.Owners, new { requestIds = requestIds.ToArray() }, cancellationToken: cancellationToken));

        return rows.ToDictionary(row => row.RequestId, row => row.BotId);
    }

    private static TInvestOrderAttemptDb ToDb(TInvestOrderAttempt attempt)
    {
        return new TInvestOrderAttemptDb
        {
            RequestId = attempt.RequestId,
            BotId = attempt.BotId,
            EngineOrderId = attempt.EngineOrderId,
            Attempt = attempt.Attempt,
            InstrumentUid = attempt.InstrumentUid,
            Side = attempt.Side.ToString(),
            OrderType = attempt.Type.ToString(),
            Lots = attempt.Lots,
            Price = attempt.Price,
            State = attempt.State.ToString(),
            ExchangeOrderId = attempt.ExchangeOrderId,
            BrokerCode = attempt.BrokerCode,
            CreatedAt = attempt.CreatedAt.UtcDateTime,
            UpdatedAt = attempt.UpdatedAt.UtcDateTime,
            WaitingCode = attempt.WaitingCode?.ToString(),
            BandLow = attempt.BandLow,
            BandHigh = attempt.BandHigh
        };
    }

    private static TInvestOrderAttempt ToAttempt(TInvestOrderAttemptDb row)
    {
        return new TInvestOrderAttempt(
            row.BotId,
            row.EngineOrderId,
            row.Attempt,
            row.RequestId,
            row.InstrumentUid,
            Enum.Parse<OrderSide>(row.Side),
            Enum.Parse<OrderType>(row.OrderType),
            row.Lots,
            row.Price,
            Enum.Parse<TInvestOrderAttemptState>(row.State),
            row.ExchangeOrderId,
            row.BrokerCode,
            new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc)),
            row.WaitingCode is null ? null : Enum.Parse<OrderWaitingCode>(row.WaitingCode),
            row.BandLow,
            row.BandHigh);
    }
}