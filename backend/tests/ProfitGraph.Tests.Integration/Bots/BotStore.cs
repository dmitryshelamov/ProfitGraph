using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Tests.Integration.Bots;

internal static class BotStore
{
    internal static async Task Add(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    /// <summary>
    ///     Changes a stored bot under its row lock, as an owner command would.
    /// </summary>
    internal static async Task Change(IHost host, BotId id, Action<Bot> change)
    {
        using IServiceScope scope = host.Services.CreateScope();
        var bots = scope.ServiceProvider.GetRequiredService<IBotRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    Bot bot = (await bots.GetForUpdate(id, CancellationToken.None))!;
                    change(bot);
                    await bots.Update(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }
}