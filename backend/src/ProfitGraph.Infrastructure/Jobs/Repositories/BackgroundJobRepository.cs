using Dapper;

using Npgsql;

using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Converters;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Jobs.Repositories.Contracts;
using ProfitGraph.Infrastructure.Jobs.Repositories.Converters;
using ProfitGraph.Infrastructure.Jobs.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Jobs.Repositories;

/// <summary>
///     Jobs for the API and the Worker. The Worker-only operations (claiming, progress, cancellation checks, recovery,
///     heartbeat) are internal to Infrastructure.
/// </summary>
internal sealed class BackgroundJobRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    BackgroundJobPayloadSerializer serializer) : IBackgroundJobRepository
{
    public async Task Add<TRequest>(BackgroundJob job, TRequest request, CancellationToken cancellationToken)
        where TRequest : IBackgroundJobRequest
    {
        BackgroundJobDb row = job.ToDb(serializer.SerializeRequest(job.Kind, request), result: null, failure: null);
        await connection.ExecuteAsync(Command(BackgroundJobQueries.Add, row, cancellationToken));
        transactions.RegisterDomainEventClear(job);
    }

    public async Task<BackgroundJob?> Get(BackgroundJobId id, CancellationToken cancellationToken)
    {
        BackgroundJobDb? row = await Find(id.Value, cancellationToken);

        return row?.ToDomain();
    }

    public async Task<BackgroundJobInternal?> GetDetails(BackgroundJobId id, CancellationToken cancellationToken)
    {
        BackgroundJobDb? row = await Find(id.Value, cancellationToken);

        if (row is null)
        {
            return null;
        }

        BackgroundJob job = row.ToDomain();

        return job.ToInternal(serializer.DeserializeResult(job.Kind, row.Result), BackgroundJobPayloadSerializer.DeserializeFailure(row.Failure));
    }

    public async Task<bool> Update(
        BackgroundJob job,
        BackgroundJobStatus expectedStatus,
        IBackgroundJobResult? result,
        BackgroundJobFailureInternal? failure,
        CancellationToken cancellationToken)
    {
        BackgroundJobDb row = job.ToDb(string.Empty, serializer.SerializeResult(job.Kind, result), BackgroundJobPayloadSerializer.SerializeFailure(failure));
        var parameters = new
        {
            row.Id,
            row.Status,
            row.Result,
            row.Failure,
            row.Completed,
            row.Total,
            row.CancelRequested,
            row.PauseRequested,
            row.Interruptions,
            row.StartedAt,
            row.FinishedAt,
            row.UpdatedAt,
            ExpectedStatus = expectedStatus.ToString(),
            SetsCancelRequest = job is { Status: BackgroundJobStatus.Running, CancelRequested: true } && expectedStatus == BackgroundJobStatus.Running,

            // A cancellation may follow a pause request, never the other way round, so only a pause alone is a new pause request.
            SetsPauseRequest = job is { Status: BackgroundJobStatus.Running, PauseRequested: true, CancelRequested: false } &&
                expectedStatus == BackgroundJobStatus.Running
        };
        int updated = await connection.ExecuteAsync(Command(BackgroundJobQueries.Update, parameters, cancellationToken));
        transactions.RegisterDomainEventClear(job);

        return updated == 1;
    }

    /// <summary>
    ///     Locks the oldest queued job of the given kinds inside the caller's transaction, or returns null when none is queued.
    /// </summary>
    internal async Task<(BackgroundJob Job, string Request)?> LockNextQueued(IReadOnlyList<string> kinds, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BackgroundJobDb>(
            Command(BackgroundJobQueries.LockNextQueued, new { Kinds = kinds.ToArray() }, cancellationToken));

        return row is null ? null : (row.ToDomain(), row.Request);
    }

    internal async Task<bool> ReportProgress(BackgroundJobId id, long completed, long total, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return await connection.ExecuteAsync(
            Command(BackgroundJobQueries.ReportProgress, new { Id = id.Value, Completed = completed, Total = total, Now = now }, cancellationToken)) == 1;
    }

    internal async Task<bool> RestartProgress(BackgroundJobId id, long completed, long total, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return await connection.ExecuteAsync(
            Command(BackgroundJobQueries.RestartProgress, new { Id = id.Value, Completed = completed, Total = total, Now = now }, cancellationToken)) == 1;
    }

    internal Task RecordStoredWork(BackgroundJobId id, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(BackgroundJobQueries.RecordStoredWork, new { Id = id.Value }, cancellationToken));
    }

    /// <summary>
    ///     Whether a running job was asked to stop for cancellation or for a pause.
    /// </summary>
    internal async Task<(bool Cancel, bool Pause)> StopRequest(BackgroundJobId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BackgroundJobStopRequestDb>(
            Command(BackgroundJobQueries.StopRequest, new { Id = id.Value }, cancellationToken));

        return row is null ? (false, false) : (row.CancelRequested, row.PauseRequested);
    }

    /// <summary>
    ///     Running jobs of the given kinds, for example those a stopped Worker left behind.
    /// </summary>
    internal async Task<IReadOnlyList<BackgroundJob>> ListRunning(IReadOnlyList<string> kinds, CancellationToken cancellationToken)
    {
        IEnumerable<BackgroundJobDb> rows = await connection.QueryAsync<BackgroundJobDb>(
            Command(BackgroundJobQueries.ListRunning, new { Kinds = kinds.ToArray() }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    internal Task Beat(string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(BackgroundJobQueries.Beat, new { Name = name, Now = now }, cancellationToken));
    }

    private Task<BackgroundJobDb?> Find(Guid id, CancellationToken cancellationToken)
    {
        return connection.QuerySingleOrDefaultAsync<BackgroundJobDb>(Command(BackgroundJobQueries.Get, new { Id = id }, cancellationToken));
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}