using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Bots.AuthorizeOfflineCarryOver;
using ProfitGraph.Application.Bots.AuthorizeWaitingRelease;
using ProfitGraph.Application.Bots.RecordOrderWaiting;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Sends the core's waiting commands, each in its own scope, so the module's worker can use them outside a runner turn.
/// </summary>
internal sealed class TInvestOrderWaits(IServiceScopeFactory scopes) : ITInvestOrderWaits
{
    public async Task Report(BotId botId, long engineOrderId, OrderWaitingCode? code, decimal? low, decimal? high, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new RecordOrderWaitingCommand(botId.Value, engineOrderId, code, Low: low, High: high), cancellationToken);
    }

    public async Task<bool> MayRelease(BotId botId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AuthorizeWaitingReleaseCommand(botId.Value), cancellationToken);
    }

    public async Task<bool> MayCarryOverOffline(BotId botId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AuthorizeOfflineCarryOverCommand(botId.Value), cancellationToken);
    }
}