using Dapper;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The order outbox delivered through the scripted gateway (stage 5.6): one place per order even after a lost response,
///     rejections reaching the engine, held submits and cancels that do not wait for them, and failing intents.
/// </summary>
[NonParallelizable]
public sealed class IntentDispatchTests
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
    public async Task SubmitIsPlacedOnceAndItsAcceptanceReachesTheEngine()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(worker, bot.Id) is [{ Status: OrderIntentStatus.Sent }], "the submit is sent");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the acceptance reaches the engine");
        await worker.StopAsync();

        scenario.Gateway.Places.ShouldBe(1);
        scenario.Gateway.LookUps.ShouldBe(0);
    }

    [Test]
    public async Task CancelNoGatewayHereCanSendDoesNotHoldOthersBack()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot elsewhere = await BotSeed.NewBot();
        await BotStore.Add(worker, elsewhere);

        for (var order = 1; order <= 25; order++)
        {
            await Add(
                worker,
                OrderIntent.Cancel(OrderIntentId.New(), elsewhere.Id, order, new ClientOrderId($"pg-elsewhere-{order}"), fromKill: true, DateTimeOffset.UtcNow));
        }

        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(worker, bot.Id) is [{ Status: OrderIntentStatus.Sent }], "the submit is sent");
        await worker.StopAsync();

        (await RuntimeScenario.Intents(worker, elsewhere.Id)).ShouldAllBe(intent => intent.Status == OrderIntentStatus.Pending);
    }

    [Test]
    public async Task SubmitThatMayHaveReachedTheVenueIsLookedUpInsteadOfPlacedAgain()
    {
        var scenario = new RuntimeScenario();
        scenario.Gateway.FailAfterPlace(count: 1);
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(worker, bot.Id) is [{ Status: OrderIntentStatus.Sent }], "the submit is resolved");
        await worker.StopAsync();

        OrderIntent intent = (await RuntimeScenario.Intents(worker, bot.Id)).Single();
        scenario.Gateway.Places.ShouldBe(1);
        scenario.Gateway.LookUps.ShouldBe(1);
        intent.Attempts.ShouldBe(1);
        intent.PlaceStartedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task VenueRejectionIsStoredAndReachesTheEngine()
    {
        var scenario = new RuntimeScenario { Gateway = { RejectWith = RejectionReason.BrokerRejected } };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Any(intent => intent.Status == OrderIntentStatus.Rejected),
            "the submit is rejected");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.Account.Orders.All(order => order.Id != 1),
            "the rejection reaches the engine");
        await worker.StopAsync();

        OrderIntent first = (await RuntimeScenario.Intents(worker, bot.Id)).First();
        first.Rejection!.Reason.ShouldBe(RejectionReason.BrokerRejected);
        (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.Account.QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public async Task StoppedBotHoldsItsSubmitWhileItsCancelsGo()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await BotStore.Change(worker, bot.Id, stored => stored.Stop(DateTimeOffset.UtcNow));
        OrderIntent submit = OrderIntent.Submit(
            OrderIntentId.New(),
            bot.Id,
            Order(id: 1),
            scenario.Gateway.ClientOrderIdFor(bot.Id, engineOrderId: 1),
            DateTimeOffset.UtcNow);
        OrderIntent sameOrderCancel = OrderIntent.Cancel(OrderIntentId.New(), bot.Id, engineOrderId: 1, submit.ClientOrderId, fromKill: false, DateTimeOffset.UtcNow);
        OrderIntent otherCancel = OrderIntent.Cancel(
            OrderIntentId.New(),
            bot.Id,
            engineOrderId: 2,
            scenario.Gateway.ClientOrderIdFor(bot.Id, engineOrderId: 2),
            fromKill: false,
            DateTimeOffset.UtcNow);
        await Add(worker, submit, sameOrderCancel, otherCancel);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).Diagnostics.HasFlag(BotDiagnostics.HeldSubmit), "the submit is held");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Count(intent => intent.Status == OrderIntentStatus.Sent) == 2,
            "the cancels are sent");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync();

        IReadOnlyList<OrderIntent> intents = await RuntimeScenario.Intents(worker, bot.Id);
        intents.Single(intent => intent.Id == submit.Id).Status.ShouldBe(OrderIntentStatus.Pending);
        scenario.Gateway.Cancels.ShouldBe([sameOrderCancel.ClientOrderId, otherCancel.ClientOrderId]);
        scenario.Gateway.Places.ShouldBe(0);
    }

    [Test]
    public async Task LastFailedAttemptFailsTheIntentAndDegradesTheBot()
    {
        var scenario = new RuntimeScenario { Gateway = { FailEveryPlace = true } };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(worker, bot.Id) is [{ Attempts: 1 }], "the first attempt fails");

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE bot_order_intents SET attempts = 9, next_attempt_at = now(), place_started_at = NULL WHERE bot_id = @id",
                new { id = bot.Id.Value });
        }

        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).StatusReason == BotStatusReason.IntentFailed, "the bot degrades");
        await worker.StopAsync();

        (await RuntimeScenario.Intents(worker, bot.Id)).Single().Status.ShouldBe(OrderIntentStatus.Failed);
        (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus.ShouldBe(BotRuntimeStatus.Degraded);
    }

    private static OrderSnapshot Order(long id)
    {
        return new OrderSnapshot(
            id,
            "entry",
            OrderSide.Buy,
            OrderType.Market,
            Quantity: null,
            LimitPrice: null,
            QuoteBudget: 10m,
            Reservation: 10m,
            "entry",
            EligibleFromCandleIndex: 1,
            OrderStatus.PendingSubmit);
    }

    private static async Task Add(IHost host, params OrderIntent[] intents)
    {
        using IServiceScope scope = host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderIntentRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    foreach (OrderIntent intent in intents)
                    {
                        await repository.TryAdd(intent, CancellationToken.None);
                    }

                    return true;
                },
                CancellationToken.None);
    }
}