using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.DispatchOrderIntent;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     Delivers due order intents of this process's bots one at a time and waits a dispatch interval when none is due.
/// </summary>
internal sealed class OrderIntentDispatchHost(
    IServiceScopeFactory scopes,
    LiveRuntimeIdentity identity,
    IOptions<BotRuntimeOptions> options,
    TimeProvider clock,
    ILogger<OrderIntentDispatchHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool delivered;

            try
            {
                using IServiceScope scope = scopes.CreateScope();
                delivered = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DispatchOrderIntentCommand(identity.HolderId.Value), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Order intent delivery failed with {ExceptionType}.", exception.GetType().Name);
                delivered = false;
            }

            if (!delivered)
            {
                try
                {
                    await Task.Delay(options.Value.DispatchInterval, clock, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}