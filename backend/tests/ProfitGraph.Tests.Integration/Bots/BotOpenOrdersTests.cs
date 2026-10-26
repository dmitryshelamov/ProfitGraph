using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotOpenOrders;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The bot's open orders come from its engine checkpoint: accepted, submitting and cancelling orders with how much has filled;
///     finished orders are left out and only the owner reads them.
/// </summary>
public sealed class BotOpenOrdersTests
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
    public async Task OpenOrdersAreTheEnginesLiveOrders_WithTheirFilledQuantity()
    {
        Bot bot = await BotSeed.NewBot();
        OrderSnapshot exit = Limit(id: 2, "ladder.exit", OrderSide.Sell, OrderStatus.Active, quantity: 832.75m, price: 0.03872m);
        OrderSnapshot average = Limit(id: 3, "ladder.average", OrderSide.Buy, OrderStatus.Active, quantity: 893.38m, price: 0.03358m);
        OrderSnapshot cancelling = Limit(id: 4, "ladder.stop", OrderSide.Sell, OrderStatus.PendingCancel, quantity: 5m, price: 0.03m);
        OrderSnapshot filled = Limit(id: 5, "ladder.old", OrderSide.Sell, OrderStatus.Filled, quantity: 1m, price: 0.04m);
        OrderSnapshot cancelled = Limit(id: 6, "ladder.old", OrderSide.Buy, OrderStatus.Cancelled, quantity: 1m, price: 0.02m);
        GraphEngineCheckpoint seeded = BotSeed.Checkpoint(nextOrderId: 7, BotSeed.MarketBuy(id: 1), average, exit, cancelling, filled, cancelled);
        GraphEngineCheckpoint engine = seeded with
        {
            Account = seeded.Account with
            {
                Progress =
                [
                    new ExternalOrderProgressCheckpoint(
                        exit.Id,
                        Confirmed: true,
                        GrossBase: 12.5m,
                        GrossQuote: 0.484m,
                        Fee: 0m,
                        BaseChange: 0m,
                        QuoteChange: 0m,
                        RealizedPnl: 0m)
                ]
            }
        };
        await Save(bot, engine);

        IReadOnlyList<BotOpenOrderInternal> open = await Open(bot.OwnerId.Value, bot.Id.Value);

        open.Select(order => order.EngineOrderId).ShouldBe([1L, 2L, 3L, 4L]);
        open.Single(order => order.EngineOrderId == 2).ShouldSatisfyAllConditions(
            order => order.Tag.ShouldBe("ladder.exit"),
            order => order.Side.ShouldBe(OrderSide.Sell),
            order => order.Type.ShouldBe(OrderType.Limit),
            order => order.Status.ShouldBe(OrderStatus.Active),
            order => order.Quantity.ShouldBe(832.75m),
            order => order.LimitPrice.ShouldBe(0.03872m),
            order => order.FilledBase.ShouldBe(12.5m));
        open.Single(order => order.EngineOrderId == 1).ShouldSatisfyAllConditions(
            order => order.Type.ShouldBe(OrderType.Market),
            order => order.Status.ShouldBe(OrderStatus.PendingSubmit),
            order => order.QuoteBudget.ShouldBe(10m),
            order => order.FilledBase.ShouldBe(0m));
        open.Single(order => order.EngineOrderId == 4).Status.ShouldBe(OrderStatus.PendingCancel);
    }

    [Test]
    public async Task ABotWithoutACheckpointHasNoOpenOrders_AndOnlyTheOwnerReadsThem()
    {
        Bot bot = await BotSeed.NewBot();
        await InTransaction(services => services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None));

        (await Open(bot.OwnerId.Value, bot.Id.Value)).ShouldBeEmpty();
        await Should.ThrowAsync<ResourceNotFoundException>(() => Open(Guid.NewGuid(), bot.Id.Value));
    }

    private static OrderSnapshot Limit(long id, string tag, OrderSide side, OrderStatus status, decimal quantity, decimal price)
    {
        return new OrderSnapshot(
            id,
            tag,
            side,
            OrderType.Limit,
            quantity,
            price,
            QuoteBudget: null,
            Reservation: quantity,
            tag,
            EligibleFromCandleIndex: 1,
            status);
    }

    private async Task Save(Bot bot, GraphEngineCheckpoint engine)
    {
        await InTransaction(async services =>
        {
            await services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);
            await services.GetRequiredService<IBotCheckpointRepository>().Save(new BotCheckpoint(bot.Id, engine, bot.Parameters, BotSeed.Now), CancellationToken.None);
        });
    }

    private async Task<IReadOnlyList<BotOpenOrderInternal>> Open(Guid ownerId, Guid botId)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotOpenOrdersQuery(ownerId, botId));
    }

    private async Task InTransaction(Func<IServiceProvider, Task> action)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await action(scope.ServiceProvider);

                    return true;
                },
                CancellationToken.None);
    }
}
