using System.Collections.Immutable;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Bots.ConfirmOrderAbsent;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The owner's confirmation that an order the venue accepted was removed by the venue and executed nothing (ADR 0028): it
///     reuses the absence confirmation, is open only to a bot stopped by an unresolved order on a venue that checks it, and is
///     refused once the engine saw any execution.
/// </summary>
public sealed class ConfirmExpiredOrderTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    private Bot _bot = null!;
    private OrderIntent _intent = null!;
    private IBotRepository _bots = null!;
    private IOrderIntentRepository _intents = null!;
    private IBotCheckpointRepository _checkpoints = null!;
    private IVenueDescriptor _venue = null!;

    [SetUp]
    public void SetUp()
    {
        _bot = BotFixture.DegradedBy(BotStatusReason.UnresolvedOrder);
        _intent = Sent(_bot.Id);
        _venue = Substitute.For<IVenueDescriptor>();
        _venue.Id.Returns(_bot.Series.Venue);
        _venue.ConfirmsExpiredOrders.Returns(true);
        _bots = Substitute.For<IBotRepository>();
        _bots.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        _intents = Substitute.For<IOrderIntentRepository>();
        _intents.GetForUpdate(_intent.Id, Arg.Any<CancellationToken>()).Returns(_intent);
        _checkpoints = Substitute.For<IBotCheckpointRepository>();
        Checkpoint(OrderStatus.Active, grossBase: 0m);
    }

    [Test]
    public async Task AnAcceptedOrderWithNoExecutionIsConfirmedExpiredAndStaysDelivered()
    {
        await Handle(_bot.OwnerId.Value);

        _intent.ShouldSatisfyAllConditions(
            () => _intent.ConfirmedAbsentAt.ShouldBe(Now),
            () => _intent.ConfirmedAbsentBy.ShouldBe(_bot.OwnerId),
            () => _intent.Status.ShouldBe(OrderIntentStatus.Sent));
        await _intents.Received(1).Update(_intent, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnOrderWithAnExecutionTheEngineSawIsRefused()
    {
        Checkpoint(OrderStatus.Active, grossBase: 1m);

        await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value));
        _intent.ConfirmedAbsentAt.ShouldBeNull();
    }

    [Test]
    public async Task AVenueThatDoesNotCheckRefuses()
    {
        _venue.ConfirmsExpiredOrders.Returns(false);

        await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value));
    }

    [Test]
    public async Task ABotThatIsNotStoppedByAnUnresolvedOrderRefuses()
    {
        _bot = BotFixture.DegradedBy(BotStatusReason.ForeignOrder);
        _intent = Sent(_bot.Id);
        _bots.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        _intents.GetForUpdate(_intent.Id, Arg.Any<CancellationToken>()).Returns(_intent);
        Checkpoint(OrderStatus.Active, grossBase: 0m);

        await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value));
    }

    [Test]
    public async Task AnOrderThatIsNoLongerWorkingRefuses()
    {
        Checkpoint(OrderStatus.Cancelled, grossBase: 0m);

        await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value));
    }

    [Test]
    public async Task ItIsConfirmedOnce()
    {
        await Handle(_bot.OwnerId.Value);

        await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value));
    }

    private Task Handle(Guid owner)
    {
        var handler = new ConfirmOrderAbsentCommandHandler(_bots, _intents, _checkpoints, new VenueRegistry([_venue]), new FakeTimeProvider(Now));

        return handler.Handle(new ConfirmOrderAbsentCommand(owner, _bot.Id.Value, _intent.Id.Value), CancellationToken.None);
    }

    private void Checkpoint(OrderStatus status, decimal grossBase)
    {
        var order = new OrderSnapshot(
            Id: 7,
            "entry",
            OrderSide.Buy,
            OrderType.Limit,
            Quantity: 10m,
            LimitPrice: 99m,
            QuoteBudget: null,
            Reservation: 990m,
            "entry",
            EligibleFromCandleIndex: 0,
            status);
        var account = new ExternalAccountCheckpoint(
            StartingCapital: 1000m,
            QuoteTotal: 1000m,
            QuoteReserved: 990m,
            BaseTotal: 0m,
            BaseReserved: 0m,
            CostBasis: 0m,
            RealizedPnl: 0m,
            FeesBase: 0m,
            FeesQuote: 0m,
            LastBuyPrice: null,
            NextOrderId: 8,
            [order],
            [new ExternalOrderProgressCheckpoint(OrderId: 7, Confirmed: true, grossBase, GrossQuote: 0m, Fee: 0m, BaseChange: 0m, QuoteChange: 0m, RealizedPnl: 0m)]);
        var engine = new GraphEngineCheckpoint(
            account,
            NextEventId: 1,
            NextAttemptId: 0,
            ImmutableDictionary<long, long>.Empty,
            [],
            new Candle(Now.AddHours(-1), Open: 1m, High: 1m, Low: 1m, Close: 1m, Volume: 0m),
            LastClosedCandleIndex: 0,
            Now,
            Started: true,
            "v1",
            ImmutableDictionary<long, string>.Empty);
        _checkpoints.Get(_bot.Id, Arg.Any<CancellationToken>()).Returns(new BotCheckpoint(_bot.Id, engine, _bot.Parameters, Now));
    }

    private static OrderIntent Sent(BotId bot)
    {
        var order = new OrderSnapshot(
            Id: 7,
            "entry",
            OrderSide.Buy,
            OrderType.Limit,
            Quantity: 10m,
            LimitPrice: 99m,
            QuoteBudget: null,
            Reservation: 990m,
            "entry",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
        OrderIntent intent = OrderIntent.Submit(OrderIntentId.New(), bot, order, new ClientOrderId("pg-test-7"), Now);
        intent.Claim(Now);
        intent.MarkPlaceStarted(Now);
        intent.MarkSent(Now);
        intent.ClearDomainEvents();

        return intent;
    }
}