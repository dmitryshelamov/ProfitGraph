using System.Globalization;
using System.Text.Json;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Bots.StopBot;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Notifications;

/// <summary>
///     Trading notifications on the scripted venue (ADR 0042): an order filled in parts, with a final notice that adds nothing,
///     queues one filled-order notification with the order's totals; the exit's sale queues its realized profit and the
///     closed position the same profit; a stop queues the request and the runtime's stop. The Engine is untouched.
/// </summary>
[NonParallelizable]
public sealed class TelegramTradingTests
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
    public async Task FilledOrdersClosedPositionAndStopAreQueuedOnceEach()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker(withOutbox: true);
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await Link(worker, bot, chatId: 910);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await Orders(worker, bot) is [{ Status: OrderStatus.Active }], "the entry is accepted");

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Partial(orderId: 1, grossBase: 0.04m, grossQuote: 4m, fee: 0.00004m));
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Partial(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Any(order => order is { Tag: "ladder.exit", Status: OrderStatus.Active }),
            "the exit is accepted");
        OrderSnapshot exit = (await Orders(worker, bot)).Single(order => order.Tag == "ladder.exit");
        decimal quantity = exit.Quantity.ShouldNotBeNull();
        decimal proceeds = quantity * exit.LimitPrice.ShouldNotBeNull();
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(exit.Id, quantity, proceeds, fee: 0.01m));
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine is { PositionOpenedAt: null } engine &&
                engine.Account.Orders.All(order => order.Id != exit.Id),
            "the exit fills");
        await Send(worker, new StopBotCommand(bot.OwnerId.Value, bot.Id.Value));
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Stopped, "the runtime stops");
        await Delivered(bot, TelegramNotificationKind.Stopped);
        await worker.StopAsync();

        IReadOnlyList<Dictionary<string, string>> filled = await Queued(bot, TelegramNotificationKind.OrderFilled);
        filled.Count.ShouldBe(2);
        Dictionary<string, string> buy = filled.Single(details => details["side"] == nameof(OrderSide.Buy));
        buy.ShouldSatisfyAllConditions(
            () => buy["engineOrderId"].ShouldBe("1"),
            () => buy["grossBase"].ShouldBe("0.1"),
            () => buy["grossQuote"].ShouldBe("10"),
            () => buy["fee"].ShouldBe("0.0001"),
            () => buy.ShouldNotContainKey("realizedPnl"));
        Dictionary<string, string> sell = filled.Single(details => details["side"] == nameof(OrderSide.Sell));
        decimal realized = decimal.Parse(sell["realizedPnl"], CultureInfo.InvariantCulture);
        realized.ShouldBeGreaterThan(0m);
        sell["tag"].ShouldBe("ladder.exit");
        decimal closed = decimal.Parse(
            (await Queued(bot, TelegramNotificationKind.PositionClosed)).Single()["realizedPnl"],
            CultureInfo.InvariantCulture);
        closed.ShouldBe(realized);
        (await Queued(bot, TelegramNotificationKind.StopRequested)).Count.ShouldBe(1);
    }

    /// <summary>
    ///     Waits until the outbox queued a notification of <paramref name="kind" />. In a full run the dispatcher first works
    ///     through what earlier tests left, so this waits longer than a runtime step.
    /// </summary>
    private static async Task Delivered(Bot bot, TelegramNotificationKind kind)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);

        while ((await Queued(bot, kind)).Count == 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
                var waiting = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE delivered_at IS NULL");
                Assert.Fail($"Timed out waiting until a {kind} notification is queued; {waiting} outbox messages wait.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task Link(IHost host, Bot bot, long chatId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>()
                        .Save(TelegramLink.Link(bot.OwnerId, chatId, DateTimeOffset.UtcNow.AddSeconds(-1)), CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private static async Task<IReadOnlyList<OrderSnapshot>> Orders(IHost host, Bot bot)
    {
        return (await RuntimeScenario.Checkpoint(host, bot.Id))?.Engine.Account.Orders ?? [];
    }

    private static async Task<IReadOnlyList<Dictionary<string, string>>> Queued(Bot bot, TelegramNotificationKind kind)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        IEnumerable<string> rows = await connection.QueryAsync<string>(
            "SELECT details::text FROM telegram_notifications WHERE bot_id = @botId AND kind = @kind",
            new { botId = bot.Id.Value, kind = kind.ToString() });

        return [.. rows.Select(row => JsonSerializer.Deserialize<Dictionary<string, string>>(row)!)];
    }

    private static async Task Send<T>(IHost host, IRequest<T> request)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}