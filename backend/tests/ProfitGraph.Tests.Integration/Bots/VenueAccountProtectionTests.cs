using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.RearmBotsForVenueAccount;
using ProfitGraph.Application.Bots.StopBotsForVenueAccount;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A venue account's protection stops the credential's bots that should run and re-arms them on resume (ADR 0022, C7).
/// </summary>
public sealed class VenueAccountProtectionTests
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
    public async Task OnlyRunningBotsOfTheCredentialStopAndComeBack()
    {
        Bot running = await Add(start: true);
        Bot stopped = await Add(start: false);
        Bot other = await Add(start: true);

        await Send(new StopBotsForVenueAccountCommand(running.CredentialId.Value));
        await Send(new StopBotsForVenueAccountCommand(stopped.CredentialId.Value));
        Bot held = await Read(running);

        held.RuntimeStatus.ShouldBe(BotRuntimeStatus.Degraded);
        held.StatusReason.ShouldBe(BotStatusReason.VenueAccountProtection);
        (await Read(stopped)).IsDegraded.ShouldBeFalse();
        (await Read(other)).IsDegraded.ShouldBeFalse();

        await Send(new RearmBotsForVenueAccountCommand(running.CredentialId.Value));
        Bot rearmed = await Read(running);

        rearmed.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created);
        rearmed.StatusReason.ShouldBeNull();
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

    private async Task Send(IRequest command)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private async Task<Bot> Read(Bot bot)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(bot.Id, CancellationToken.None)).ShouldNotBeNull();
    }
}