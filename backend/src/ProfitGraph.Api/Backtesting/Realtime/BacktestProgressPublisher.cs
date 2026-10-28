using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Common.Realtime;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Api.Backtesting.Realtime;

/// <summary>
///     Turns the Worker's commit signals into hub messages (stage 3 plan, S3-11): for each followed run it reads the
///     observations committed after the last one sent and sends them in order, then <c>BacktestFinished</c> once the run
///     has a result. Signals are handled one at a time, so a run's messages never overtake each other. The Worker stores
///     the result only after the last observation, so a result read before the observations means none is left behind.
/// </summary>
public sealed class BacktestProgressPublisher(
    IBacktestProgressNotifications notifications,
    BacktestProgressSubscriptions subscriptions,
    IHubContext<BacktestHub> hub,
    IServiceScopeFactory scopes,
    IOptions<RealtimeOptions> options,
    ILogger<BacktestProgressPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (BacktestRunId runId in notifications.Listen(stoppingToken))
                {
                    await Publish(runId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Listening for backtest progress failed; listening again.");
                await Task.Delay(options.Value.RetryDelay, stoppingToken);
            }
        }
    }

    private async Task Publish(BacktestRunId runId, CancellationToken cancellationToken)
    {
        if (!subscriptions.TryGetSent(runId.Value, out long sent))
        {
            return;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IBacktestReader>();
        IClientProxy group = hub.Clients.Group(BacktestProgressSubscriptions.Group(runId.Value));

        // Read before the observations: a result stored while they are sent would otherwise end the run without them.
        BacktestRunStateInternal state = await reader.State(runId, cancellationToken);
        long? after = sent;

        while (after is { } cursor)
        {
            BacktestPageInternal<BacktestObservationInternal> page = await reader.Observations(runId, cursor, options.Value.BacktestProgressPageSize, cancellationToken);

            foreach (BacktestObservationInternal observation in page.Items)
            {
                await group.SendAsync("BacktestProgress", new BacktestProgressResponse(runId.Value, observation.ToResponse()), cancellationToken);
                subscriptions.MarkSent(runId.Value, observation.Sequence);
            }

            after = page.Next;
        }

        if (state.Outcome is { } outcome)
        {
            await group.SendAsync("BacktestFinished", new BacktestFinishedResponse(runId.Value, outcome), cancellationToken);
            subscriptions.Forget(runId.Value);
        }
    }
}