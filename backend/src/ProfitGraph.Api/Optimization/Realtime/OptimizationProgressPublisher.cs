using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Api.Optimization.Converters;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Api.Optimization.Realtime;

/// <summary>
///     Turns the Worker's commit signals into hub messages (stage 4 plan, S4-12): for each followed optimization it reads
///     the trials stored after the last one sent and sends them in order, then <c>OptimizationPaused</c> when its job
///     pauses and <c>OptimizationFinished</c> once it has ended. Signals are handled one at a time, so an optimization's messages never overtake each other.
/// </summary>
public sealed class OptimizationProgressPublisher(
    IOptimizationProgressNotifications notifications,
    OptimizationProgressSubscriptions subscriptions,
    IHubContext<OptimizationHub> hub,
    IServiceScopeFactory scopes,
    ILogger<OptimizationProgressPublisher> logger) : BackgroundService
{
    private const int PageSize = 500;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (OptimizationId id in notifications.Listen(stoppingToken))
                {
                    await Publish(id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Listening for optimization progress failed; listening again.");
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task Publish(OptimizationId id, CancellationToken cancellationToken)
    {
        if (!subscriptions.TryGetSent(id.Value, out long sent))
        {
            return;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IOptimizationReader>();
        IClientProxy group = hub.Clients.Group(OptimizationProgressSubscriptions.Group(id.Value));
        long? after = sent;

        while (after is { } cursor)
        {
            OptimizationTrialPageInternal page = await reader.Trials(id, cursor, PageSize, cancellationToken);

            foreach (OptimizationTrialInternal trial in page.Items)
            {
                await group.SendAsync("OptimizationProgress", new OptimizationProgressResponse(id.Value, trial.ToResponse()), cancellationToken);
                subscriptions.MarkSent(id.Value, trial.Sequence);
            }

            after = page.Next;
        }

        OptimizationCursorInternal state = await reader.Cursor(id, cancellationToken);

        if (state.IsFinished)
        {
            await group.SendAsync("OptimizationFinished", new OptimizationFinishedResponse(id.Value, state.Status), cancellationToken);
            subscriptions.Forget(id.Value);
        }
        else if (state.Status == "Paused")
        {
            // A paused optimization stays followed: on resume its trials continue after the last one sent.
            await group.SendAsync("OptimizationPaused", new OptimizationFinishedResponse(id.Value, state.Status), cancellationToken);
        }
    }
}