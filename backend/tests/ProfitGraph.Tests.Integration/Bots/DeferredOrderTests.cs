using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotWaitingOrders;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A submit the venue is not ready for is held in the core and shown as a wait, sent once the venue is ready, and cancelled
///     locally by a cancel (ADR 0024).
/// </summary>
[NonParallelizable]
public sealed class DeferredOrderTests
{
    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [SetUp]
    public Task Isolate()
    {
        return RuntimeScenario.StopEveryBot();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task ASubmitTheVenueIsNotReadyForWaitsAndIsSentOnceItIs()
    {
        var scenario = new RuntimeScenario { Readiness = { Hold = OrderWaitingCode.SettlementHold } };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Any(intent => intent.Status == OrderIntentStatus.Deferred),
            "the submit is deferred");

        OrderIntent deferred = (await RuntimeScenario.Intents(worker, bot.Id)).Single();
        scenario.Gateway.Places.ShouldBe(0);
        deferred.Deferral!.Code.ShouldBe(OrderWaitingCode.SettlementHold);
        (await Waiting(worker, bot)).ShouldHaveSingleItem().Code.ShouldBe(OrderWaitingCode.SettlementHold);

        scenario.Readiness.Hold = null;
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Any(intent => intent.Status == OrderIntentStatus.Sent),
            "the submit is sent once the venue is ready");
        await worker.StopAsync();

        scenario.Gateway.Places.ShouldBe(1);
        (await Waiting(worker, bot)).ShouldBeEmpty();
    }

    [Test]
    public async Task ACancelOfADeferredSubmitIsLocal()
    {
        var scenario = new RuntimeScenario { Readiness = { Hold = OrderWaitingCode.SettlementHold } };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Any(intent => intent.Status == OrderIntentStatus.Deferred),
            "the submit is deferred");

        using (IServiceScope scope = worker.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IOrderIntentRepository>();
            await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
                .ExecuteAsync(
                    async () => await repository.TryAdd(
                        OrderIntent.Cancel(
                            OrderIntentId.New(),
                            bot.Id,
                            engineOrderId: 1,
                            scenario.Gateway.ClientOrderIdFor(bot.Id, engineOrderId: 1),
                            fromKill: false,
                            DateTimeOffset.UtcNow),
                        CancellationToken.None),
                    CancellationToken.None);
        }

        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Any(intent => intent is { Kind: OrderIntentKind.Cancel, Status: OrderIntentStatus.Sent }),
            "the cancel is handled");
        await worker.StopAsync();

        IReadOnlyList<OrderIntent> intents = await RuntimeScenario.Intents(worker, bot.Id);
        intents.Single(intent => intent.Kind == OrderIntentKind.Submit).Rejection!.Reason.ShouldBe(RejectionReason.CancelledBeforeSend);
        scenario.Gateway.Cancels.ShouldBeEmpty();
        scenario.Gateway.Places.ShouldBe(0);
        (await Waiting(worker, bot)).ShouldBeEmpty();
    }

    private static async Task<IReadOnlyList<BotOrderWaitingInternal>> Waiting(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotWaitingOrdersQuery(bot.OwnerId.Value, bot.Id.Value));
    }
}