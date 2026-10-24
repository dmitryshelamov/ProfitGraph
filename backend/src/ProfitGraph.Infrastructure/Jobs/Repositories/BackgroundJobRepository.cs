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
            row.StartedAt,
            row.FinishedAt,
            row.UpdatedAt,
            ExpectedStatus = expectedStatus.ToString(),
            SetsCancelRequest = job is { Status: BackgroundJobStatus.Running, CancelRequested: true }
        };
        int updated = await connection.ExecuteAsync(Command(BackgroundJobQueries.Update, parameters, cancellationToken));
        transactions.RegisterDomainEventClear(job);

        return updated == 1;
    }

    /// <summary>
    ///     Locks the oldest queued job inside the caller's transaction, or returns null when the queue is empty.
    /// </summary>
    internal async Task<(BackgroundJob Job, string Request)?> LockNextQueued(CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BackgroundJobDb>(Command(BackgroundJobQueries.LockNextQueued, parameters: null, cancellationToken));

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

    internal async Task<bool> IsCancelRequested(BackgroundJobId id, CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<bool>(Command(BackgroundJobQueries.IsCancelRequested, new { Id = id.Value }, cancellationToken));
    }

    /// <summary>
    ///     Fails running jobs left by a stopped Worker: every running job, or only the given one.
    /// </summary>
    internal Task<int> InterruptRunning(BackgroundJobFailureInternal failure, DateTimeOffset now, CancellationToken cancellationToken, BackgroundJobId? id = null)
    {
        var parameters = new { Failure = BackgroundJobPayloadSerializer.SerializeFailure(failure), Now = now, Id = id?.Value };

        return connection.ExecuteAsync(Command(BackgroundJobQueries.InterruptRunning, parameters, cancellationToken));
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