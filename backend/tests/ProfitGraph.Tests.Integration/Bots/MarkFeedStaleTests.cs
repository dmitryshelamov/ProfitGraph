using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.MarkFeedStale;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A stalled live feed marks every bot that should run on its series, and only those.
/// </summary>
public sealed class MarkFeedStaleTests
{
    private ProfitGraphApiFactory _api = null!;
    private IHost _host = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
        _host = BotTestHost.Create();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _host.Dispose();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task StaleFeedMarksRunningBotsOfItsSeriesUntilItRecovers()
    {
        Bot running = await Add(start: true);
        Bot stopped = await Add(start: false);

        await Send(stale: true);
        Bot staleRunning = await Read(running);
        Bot staleStopped = await Read(stopped);
        await Send(stale: false);

        staleRunning.Diagnostics.HasFlag(BotDiagnostics.StaleFeed).ShouldBeTrue();
        staleStopped.Diagnostics.HasFlag(BotDiagnostics.StaleFeed).ShouldBeFalse();
        (await Read(running)).Diagnostics.HasFlag(BotDiagnostics.StaleFeed).ShouldBeFalse();
    }

    private async Task<Bot> Add(bool start)
    {
        Bot bot = await BotSeed.NewBot();

        if (start)
        {
            bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
        }

        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return bot;
    }

    private async Task Send(bool stale)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new MarkFeedStaleCommand("bybit", "BTCUSDT", "1h", stale));
    }

    private async Task<Bot> Read(Bot bot)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(bot.Id, CancellationToken.None)).ShouldNotBeNull();
    }
}