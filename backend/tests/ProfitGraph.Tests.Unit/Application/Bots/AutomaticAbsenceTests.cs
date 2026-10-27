using System.Collections.Immutable;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Bots.ApplyBotReconciliation;
using ProfitGraph.Application.Bots.AuthorizeOfflineCarryOver;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.RecheckUnresolvedOrders;
using ProfitGraph.Application.Bots.RecordBotFeed;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     Orders the venue's gateway closes on its own evidence (ADR 0030): the reconciliation marks the intent and its feed entry
///     lists the checks that justified it; a stopped bot is checked again and returned to the runtime only when the gateway
///     would now close every unresolved order, and only if its owner wants it to run; a bot nobody runs may have its day
///     orders carried over only while nothing but a failed run holds it.
/// </summary>
public sealed class AutomaticAbsenceTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;
    private static readonly string[] Checks = ["NotFoundByRequestId", "NotAmongActiveOrders", "TradingDayEnded"];

    private Bot _bot = null!;
    private IBotRepository _bots = null!;
    private IBotLeaseRepository _leases = null!;
    private IOrderIntentRepository _intents = null!;
    private IBotCheckpointRepository _checkpoints = null!;
    private ITradingHaltRepository _halts = null!;
    private IOrderGateway _gateway = null!;
    private OrderIntent _intent = null!;

    [SetUp]
    public void SetUp()
    {
        _bot = BotFixture.DegradedBy(BotStatusReason.UnresolvedOrder);
        _intent = Sent(_bot.Id);
        _bots = Substitute.For<IBotRepository>();
        _bots.Get(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        _bots.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        _intents = Substitute.For<IOrderIntentRepository>();
        _intents.ListByBot(_bot.Id, Arg.Any<CancellationToken>()).Returns([_intent]);
        _intents.GetForUpdate(_intent.Id, Arg.Any<CancellationToken>()).Returns(_intent);
        _leases = Substitute.For<IBotLeaseRepository>();
        _checkpoints = Substitute.For<IBotCheckpointRepository>();
        _checkpoints.Get(_bot.Id, Arg.Any<CancellationToken>()).Returns(Checkpoint());
        _halts = Substitute.For<ITradingHaltRepository>();
        _halts.Get(Arg.Any<CancellationToken>()).Returns(TradingHalt.Restore(isHalted: false, Now));
        _gateway = Substitute.For<IOrderGateway>();
        _gateway.Venue.Returns(_bot.Series.Venue);
        _gateway.ClientOrderIdFor(_bot.Id, engineOrderId: 7).Returns(_intent.ClientOrderId);
    }

    [Test]
    public async Task AnOrderTheGatewayClosedIsMarkedOnItsIntentWithTheChecksAndTheDegradationDetailIsKept()
    {
        var leaseHolder = LeaseHolderId.New();
        _bot = BotFixture.Running();
        _bots.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        _intent = Sent(_bot.Id);
        _intents.ListByBot(_bot.Id, Arg.Any<CancellationToken>()).Returns([_intent]);
        _intents.GetForUpdate(_intent.Id, Arg.Any<CancellationToken>()).Returns(_intent);
        _leases.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(BotLease.Acquire(_bot.Id, leaseHolder, Now, TimeSpan.FromMinutes(1)));

        bool recorded = await new ApplyBotReconciliationCommandHandler(_bots, _leases, _intents, new FakeTimeProvider(Now)).Handle(
            new ApplyBotReconciliationCommand(
                _bot.Id.Value,
                leaseHolder.Value,
                Epoch: 1,
                [_intent.ClientOrderId.Value],
                [_intent.ClientOrderId.Value],
                Degradation: null,
                DegradationDetail: null,
                new Dictionary<string, IReadOnlyList<string>> { [_intent.ClientOrderId.Value] = Checks }),
            CancellationToken.None);

        recorded.ShouldBeTrue();
        _intent.ShouldSatisfyAllConditions(
            () => _intent.ConfirmedAbsentAutomatically.ShouldBeTrue(),
            () => _intent.ConfirmedAbsentBy.ShouldBeNull(),
            () => _intent.DomainEvents.OfType<OrderIntentAbsenceProven>().ShouldHaveSingleItem().Checks.ShouldBe(Checks));
        await _intents.Received(1).Update(_intent, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ADegradationCarriesTheFailedCheckToTheBot()
    {
        var leaseHolder = LeaseHolderId.New();
        _bot = BotFixture.Running();
        _bots.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(_bot);
        _leases.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(BotLease.Acquire(_bot.Id, leaseHolder, Now, TimeSpan.FromMinutes(1)));
        _intents.ListByBot(_bot.Id, Arg.Any<CancellationToken>()).Returns([]);

        await new ApplyBotReconciliationCommandHandler(_bots, _leases, _intents, new FakeTimeProvider(Now)).Handle(
            new ApplyBotReconciliationCommand(_bot.Id.Value, leaseHolder.Value, Epoch: 1, [], [], BotStatusReason.UnresolvedOrder, "AutoCheck:PositionDiffers"),
            CancellationToken.None);

        _bot.StatusReason.ShouldBe(BotStatusReason.UnresolvedOrder);
        _bot.DomainEvents.OfType<BotDegraded>().ShouldHaveSingleItem().Detail.ShouldBe("AutoCheck:PositionDiffers");
    }

    [Test]
    public async Task TheFeedListsTheChecksThatClosedTheOrder()
    {
        var feed = new RecordingFeed();

        await new RecordBotFeedHandler(feed).Handle(new OrderIntentAbsenceProven(_bot.Id, _intent.Id, EngineOrderId: 7, Checks, Now), CancellationToken.None);

        BotFeedEntry entry = feed.Entries.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(BotFeedKind.OrderAbsenceProven);
        entry.Details.ShouldBe(
            new Dictionary<string, string> { ["engineOrderId"] = "7", ["checks"] = "NotFoundByRequestId,NotAmongActiveOrders,TradingDayEnded" },
            ignoreOrder: true);
    }

    [Test]
    public async Task AStoppedBotReturnsToTheRuntimeWhenTheGatewayWouldCloseEveryOrder()
    {
        _gateway.CanProveAbsence(Arg.Any<BotReconcileInternal>(), Arg.Any<CancellationToken>()).Returns(true);

        bool returned = await Recheck();

        returned.ShouldBeTrue();
        _bot.ShouldSatisfyAllConditions(
            () => _bot.RuntimeStatus.ShouldBe(BotRuntimeStatus.Created),
            () => _bot.StatusReason.ShouldBeNull(),
            () => _bot.DesiredState.ShouldBe(BotDesiredState.Running));
        await _bots.Received(1).Update(_bot, Arg.Any<CancellationToken>());
        await _gateway.Received(1)
            .CanProveAbsence(
                Arg.Is<BotReconcileInternal>(request => request != null && request.ExpectedBase == 0m && request.Orders.Single().EngineOrderId == 7 &&
                    request.Orders.Single().PlaceStartedAt == Now),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AStoppedBotStaysWhenTheGatewayCannotProveEveryOrder()
    {
        _gateway.CanProveAbsence(Arg.Any<BotReconcileInternal>(), Arg.Any<CancellationToken>()).Returns(false);

        (await Recheck()).ShouldBeFalse();
        _bot.StatusReason.ShouldBe(BotStatusReason.UnresolvedOrder);
        await _bots.DidNotReceive().Update(Arg.Any<Bot>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ABotTheOwnerStoppedIsNeverStartedByTheCheck()
    {
        _bot.Stop(Now);
        _gateway.CanProveAbsence(Arg.Any<BotReconcileInternal>(), Arg.Any<CancellationToken>()).Returns(true);

        (await Recheck()).ShouldBeFalse();
        _bot.ShouldSatisfyAllConditions(
            () => _bot.DesiredState.ShouldBe(BotDesiredState.Stopped),
            () => _bot.StatusReason.ShouldBe(BotStatusReason.UnresolvedOrder));
        await _gateway.DidNotReceive().CanProveAbsence(Arg.Any<BotReconcileInternal>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ABotStoppedForAnotherReasonOrWithoutAGatewayIsLeftAlone()
    {
        Bot foreign = BotFixture.DegradedBy(BotStatusReason.ForeignOrder);
        _bots.Get(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        _gateway.CanProveAbsence(Arg.Any<BotReconcileInternal>(), Arg.Any<CancellationToken>()).Returns(true);

        (await Recheck(foreign.Id.Value)).ShouldBeFalse();
        (await Recheck(new VenuePorts<IOrderGateway>([]))).ShouldBeFalse();
        await _gateway.DidNotReceive().CanProveAbsence(Arg.Any<BotReconcileInternal>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ABotWithNoOrderToCheckIsLeftAlone()
    {
        _intents.ListByBot(_bot.Id, Arg.Any<CancellationToken>()).Returns([]);
        _checkpoints.Get(_bot.Id, Arg.Any<CancellationToken>()).Returns((BotCheckpoint?)null);
        _gateway.CanProveAbsence(Arg.Any<BotReconcileInternal>(), Arg.Any<CancellationToken>()).Returns(true);

        (await Recheck()).ShouldBeFalse();
    }

    [Test]
    public void ABotWhoseRunFailedMayHaveItsDayOrdersCarriedOverWithNoRunner()
    {
        _bot = BotFixture.FailedOnce();
        Authorize(_bot, lease: null).ShouldBeTrue();
        Authorize(_bot, BotLease.Acquire(_bot.Id, LeaseHolderId.New(), Now.AddHours(-1), TimeSpan.FromMinutes(1))).ShouldBeTrue();
    }

    [Test]
    public void ACarryOverIsRefusedWhenARunnerHoldsTheBotOrTheOwnerOrASafetyReasonStoppedIt()
    {
        Bot running = BotFixture.Running();
        Authorize(running, BotLease.Acquire(running.Id, LeaseHolderId.New(), Now, TimeSpan.FromMinutes(1))).ShouldBeFalse();
        Bot stopped = BotFixture.Running();
        stopped.Stop(Now);
        Authorize(stopped, lease: null).ShouldBeFalse();
        Authorize(BotFixture.DegradedBy(BotStatusReason.UnresolvedOrder), lease: null).ShouldBeFalse();
        Authorize(BotFixture.DegradedBy(BotStatusReason.ForeignOrder), lease: null).ShouldBeFalse();
        Bot killed = BotFixture.Running();
        killed.Kill(BotStatusReason.Killed, Now);
        Authorize(killed, lease: null).ShouldBeFalse();
        Authorize(BotFixture.FailedOnce(), lease: null, halted: true).ShouldBeFalse();
    }

    private bool Authorize(Bot bot, BotLease? lease, bool halted = false)
    {
        _bots.GetForUpdate(bot.Id, Arg.Any<CancellationToken>()).Returns(bot);
        _leases.GetForUpdate(bot.Id, Arg.Any<CancellationToken>()).Returns(lease);
        _halts.Get(Arg.Any<CancellationToken>()).Returns(TradingHalt.Restore(halted, Now));

        return new AuthorizeOfflineCarryOverCommandHandler(_leases, _bots, _halts, new FakeTimeProvider(Now))
            .Handle(new AuthorizeOfflineCarryOverCommand(bot.Id.Value), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private Task<bool> Recheck()
    {
        return Recheck(_bot.Id.Value);
    }

    private Task<bool> Recheck(Guid botId)
    {
        return Recheck(botId, new VenuePorts<IOrderGateway>([_gateway]));
    }

    private Task<bool> Recheck(VenuePorts<IOrderGateway> gateways)
    {
        return Recheck(_bot.Id.Value, gateways);
    }

    private Task<bool> Recheck(Guid botId, VenuePorts<IOrderGateway> gateways)
    {
        return new RecheckUnresolvedOrdersCommandHandler(_bots, _checkpoints, _intents, gateways, new FakeTimeProvider(Now)).Handle(
            new RecheckUnresolvedOrdersCommand(botId),
            CancellationToken.None);
    }

    private BotCheckpoint Checkpoint()
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
            OrderStatus.Active);
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
            [new ExternalOrderProgressCheckpoint(OrderId: 7, Confirmed: true, GrossBase: 0m, GrossQuote: 0m, Fee: 0m, BaseChange: 0m, QuoteChange: 0m, RealizedPnl: 0m)]);
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

        return new BotCheckpoint(_bot.Id, engine, _bot.Parameters, Now);
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
