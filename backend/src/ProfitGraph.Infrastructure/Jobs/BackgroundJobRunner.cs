using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Engine.Common;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Jobs.Repositories;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     Claims the oldest queued job and runs its request in this DI scope (ADR 0011). The transaction lasts only for the
///     claim; the outcome is written by a conditional update from Running. A transient database failure while the outcome is
///     written is retried on every polling interval until the Worker stops, so a running Worker never leaves a finished job
///     Running.
/// </summary>
internal sealed class BackgroundJobRunner(
    BackgroundJobRepository repository,
    PostgresTransactionExecutor transactions,
    BackgroundJobPayloadSerializer serializer,
    BackgroundJobProgressReporter progress,
    ISender sender,
    IServiceScopeFactory scopes,
    IOptions<BackgroundJobOptions> options,
    IEnumerable<BackgroundJobRegistration> registrations,
    TimeProvider clock,
    ILogger<BackgroundJobRunner> logger)
{
    internal static readonly BackgroundJobFailureInternal RestartFailure =
        new BackgroundJobFailureInternal(BackgroundJobErrorCodes.Interrupted, "The job was interrupted by a Worker restart.");

    private static readonly BackgroundJobFailureInternal ShutdownFailure =
        new BackgroundJobFailureInternal(BackgroundJobErrorCodes.Interrupted, "The job was interrupted by a Worker shutdown.");

    private static readonly BackgroundJobFailureInternal UnexpectedFailure =
        new BackgroundJobFailureInternal(BackgroundJobErrorCodes.Unexpected, "An unexpected error occurred.");

    private readonly BackgroundJobFailureInternal _stalledFailure = new BackgroundJobFailureInternal(
        BackgroundJobErrorCodes.Interrupted,
        $"The job was interrupted {options.Value.InterruptionLimit} times in a row without making progress.");

    private readonly HashSet<string> _resumable = [.. registrations.Where(registration => registration.Resumable).Select(registration => registration.Kind.Value)];

    /// <summary>
    ///     Runs one job of the given kinds and returns true, or returns false when none is queued.
    /// </summary>
    internal async Task<bool> RunNext(IReadOnlyList<string> kinds, CancellationToken stoppingToken)
    {
        (BackgroundJob Job, string Request)? claimed = await transactions.ExecuteAsync(() => Claim(kinds), stoppingToken);

        if (claimed is null)
        {
            return false;
        }

        using (logger.BeginScope(new Dictionary<string, object> { ["JobId"] = claimed.Value.Job.Id.Value, ["JobKind"] = claimed.Value.Job.Kind.Value }))
        {
            await Run(claimed.Value.Job, claimed.Value.Request, stoppingToken);
        }

        return true;
    }

    private async Task<(BackgroundJob Job, string Request)?> Claim(IReadOnlyList<string> kinds)
    {
        (BackgroundJob Job, string Request)? next = await repository.LockNextQueued(kinds, CancellationToken.None);

        if (next is null)
        {
            return null;
        }

        next.Value.Job.Start(clock.GetUtcNow());
        await repository.Update(next.Value.Job, BackgroundJobStatus.Queued, result: null, failure: null, CancellationToken.None);

        return next;
    }

    private async Task Run(BackgroundJob job, string request, CancellationToken stoppingToken)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var watcherStop = new CancellationTokenSource();
        Task watcher = new BackgroundJobStopWatcher(token => StopRequest(job.Id, token), options.Value.PollingInterval, clock, logger)
            .Watch(work, watcherStop.Token);
        progress.Attach(job);
        IBackgroundJobResult? result = null;
        BackgroundJobFailureInternal? failure = null;

        try
        {
            object? response = await sender.Send(serializer.DeserializeRequest(job.Kind, request), work.Token);
            result = response as IBackgroundJobResult ?? throw new InvalidOperationException("A background job request returned no result.");
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            // The watcher or the host stopped the work; the outcome is decided below.
        }
        catch (Exception exception)
        {
            failure = Failure(exception);
        }

        await watcherStop.CancelAsync();
        await watcher;
        DateTimeOffset now = clock.GetUtcNow();

        if (failure is not null)
        {
            job.Fail(now);
        }
        else if (result is null)
        {
            // Stopped without a result: decide on the stored requests, which the watcher may not have seen yet.
            BackgroundJob current = await Persist(() => repository.Get(job.Id, CancellationToken.None), stoppingToken) ?? job;

            if (current.CancelRequested)
            {
                current.ConfirmCancelled(now);
            }
            else if (current.PauseRequested)
            {
                current.ConfirmPaused(now);
            }
            else
            {
                await Interrupt(current, ShutdownFailure, stoppingToken);

                return;
            }

            job = current;
        }
        else
        {
            job.Complete(now);
        }

        if (!await Persist(() => repository.Update(job, BackgroundJobStatus.Running, result, failure, CancellationToken.None), stoppingToken))
        {
            logger.LogWarning("The job outcome was not saved because another transition won. Status={Status}.", job.Status);
        }
    }

    /// <summary>
    ///     Records that a stopped Worker interrupted a running job: a resumable job is queued again unless it stalled, any
    ///     other job fails with <paramref name="failure" />.
    /// </summary>
    internal async Task Interrupt(BackgroundJob job, BackgroundJobFailureInternal failure, CancellationToken stoppingToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        BackgroundJobFailureInternal? stored = null;

        if (!_resumable.Contains(job.Kind.Value))
        {
            job.Fail(now);
            stored = failure;
        }
        else if (!job.Interrupt(options.Value.InterruptionLimit, now))
        {
            stored = _stalledFailure;
        }

        if (!await Persist(() => repository.Update(job, BackgroundJobStatus.Running, result: null, stored, CancellationToken.None), stoppingToken))
        {
            logger.LogWarning("The interruption of job {JobId} was not saved because another transition won.", job.Id.Value);
        }
    }

    /// <summary>
    ///     Reads the stop requests in its own scope, because the work uses this scope's connection meanwhile.
    /// </summary>
    private async Task<(bool Cancel, bool Pause)> StopRequest(BackgroundJobId id, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<BackgroundJobRepository>().StopRequest(id, cancellationToken);
    }

    /// <summary>
    ///     Writes the job's state, retrying on every polling interval while the database fails transiently. A stopping Worker
    ///     gives up after the attempt in progress; its next start recovers a job left Running.
    /// </summary>
    private async Task<T> Persist<T>(Func<Task<T>> write, CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                return await write();
            }
            catch (NpgsqlException exception) when (exception.IsTransient && !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Saving the job's state failed with {ExceptionType}; it is retried.", exception.GetType().Name);
            }

            await Task.Delay(options.Value.PollingInterval, clock, stoppingToken);
        }
    }

    private BackgroundJobFailureInternal Failure(Exception exception)
    {
        switch (exception)
        {
            case ApplicationValidationException validation:
                return new BackgroundJobFailureInternal(validation.Code, string.Join(" ", validation.Errors.Select(error => $"{error.Field}: {error.Message}")));
            case ApplicationExceptionBase application:
                return new BackgroundJobFailureInternal(application.Code, application.Message);
            case DomainException domain:
                return new BackgroundJobFailureInternal(domain.Code, domain.Message);
            case EngineException engine:
                return new BackgroundJobFailureInternal("domain_rule_violation", engine.Message);
            default:
                logger.LogError(exception, "A background job failed unexpectedly.");

                return UnexpectedFailure;
        }
    }
}