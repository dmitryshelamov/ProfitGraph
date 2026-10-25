using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotWaitingOrders;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     Steam's waiting codes reach the core's neutral waiting records of a bot's orders (ADR 0022, ADR 0023 TC4).
/// </summary>
public sealed class SteamOrderWaitingTests
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
    public async Task AHoldThenAConfirmationThenNothing()
    {
        Bot bot = await BotSeed.NewBot();

        using (IServiceScope scope = _host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
                .ExecuteAsync(
                    async () =>
                    {
                        await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                        return true;
                    },
                    CancellationToken.None);
        }

        var waiting = new SteamOrderWaiting(_host.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SteamOrderWaiting>.Instance);
        SteamOrderJournalEntry entry = Entry(bot);
        DateTimeOffset unlock = BotSeed.Now.AddDays(7);

        await waiting.Report(entry, SteamWaitingCode.SettlementHold, unlock, CancellationToken.None);
        IReadOnlyList<BotOrderWaitingInternal> held = await Waiting(bot);
        await waiting.Report(entry, SteamWaitingCode.AwaitingConfirmation, unlock, CancellationToken.None);
        IReadOnlyList<BotOrderWaitingInternal> confirming = await Waiting(bot);
        await waiting.Report(entry, code: null, until: null, CancellationToken.None);

        held.Single()
            .ShouldSatisfyAllConditions(
                order => order.EngineOrderId.ShouldBe(4),
                order => order.Code.ShouldBe(OrderWaitingCode.SettlementHold),
                order => order.Until.ShouldBe(unlock));
        confirming.Single()
            .ShouldSatisfyAllConditions(
                order => order.Code.ShouldBe(OrderWaitingCode.AwaitingConfirmation),
                order => order.Until.ShouldBeNull());
        (await Waiting(bot)).ShouldBeEmpty();
    }

    private async Task<IReadOnlyList<BotOrderWaitingInternal>> Waiting(Bot bot)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotWaitingOrdersQuery(bot.OwnerId.Value, bot.Id.Value));
    }

    private static SteamOrderJournalEntry Entry(Bot bot)
    {
        return new SteamOrderJournalEntry(
            "pg-w",
            bot.Id.Value,
            bot.CredentialId.Value,
            EngineOrderId: 4,
            "KZT:730:Dreams & Nightmares Case",
            OrderSide.Sell,
            OrderType.Limit,
            Quantity: 1,
            PriceMinorUnits: 50000,
            SteamOrderJournalState.Waiting,
            WaitingCode: null,
            SteamOrderId: null,
            ConfirmationCreator: null,
            Outcome: null,
            PlaceStartedAt: null,
            Deadline: null,
            BotSeed.Now,
            BotSeed.Now);
    }
}