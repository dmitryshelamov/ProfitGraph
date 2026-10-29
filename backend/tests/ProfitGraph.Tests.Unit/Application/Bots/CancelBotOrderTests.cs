using System.Collections.Immutable;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Bots.CancelBotOrder;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetOrderCancel;
using ProfitGraph.Application.Bots.RecordBotFeed;
using ProfitGraph.Application.Bots.ResolveOrderCancel;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Feed;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.OrderCancels;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The owner's cancel of one working order of a running bot (ADR 0048): stored for the runner only for an order the bot's last
///     checkpoint holds as working, one at a time per bot, read back by its owner and recorded in the feed when the engine took it.
///     A stopped bot without a runner (ADR 0055) gets a cancel intent at once for an order that left for the venue.
/// </summary>
public sealed class CancelBotOrderTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    private Bot _bot = null!;
    private IBotRepository _bots = null!;
    private IBotCheckpointRepository _checkpoints = null!;
    private IOrderCancelRequestRepository _cancels = null!;
    private IOrderIntentRepository _intents = null!;
    private IBotLeaseRepository _leases = null!;
    private OrderSnapshot _order = null!;

    [SetUp]
    public void SetUp()
    {
        _bots = Substitute.For<IBotRepository>();
        _leases = Substitute.For<IBotLeaseRepository>();
        UseBot(BotFixture.Running(), leaseHeld: true);
        _checkpoints = Substitute.For<IBotCheckpointRepository>();
        _cancels = Substitute.For<IOrderCancelRequestRepository>();
        _cancels.TryAdd(Arg.Any<OrderCancelRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        _intents = Substitute.For<IOrderIntentRepository>();
        _intents.ListByBot(Arg.Any<BotId>(), Arg.Any<CancellationToken>()).Returns([]);
        _intents.TryAdd(Arg.Any<OrderIntent>(), Arg.Any<CancellationToken>()).Returns(true);
        Checkpoint(OrderStatus.Active);
    }

    [Test]
    public async Task AWorkingOrderOfARunningBotIsStoredForTheRunner()
    {
        OrderCancelInternal cancel = await Handle(_bot.OwnerId.Value, engineOrderId: 7);

        cancel.ShouldSatisfyAllConditions(
            () => cancel.BotId.ShouldBe(_bot.Id.Value),
            () => cancel.EngineOrderId.ShouldBe(7),
            () => cancel.Status.ShouldBe(OrderCancelStatus.Pending),
            () => cancel.CreatedAt.ShouldBe(Now));
        await _cancels.Received(1).TryAdd(Arg.Is<OrderCancelRequest>(request => request!.EngineOrderId == 7 && request.BotId == _bot.Id), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnOrderStillBeingSubmittedCanBeCancelled()
    {
        Checkpoint(OrderStatus.PendingSubmit);

        (await Handle(_bot.OwnerId.Value, engineOrderId: 7)).Status.ShouldBe(OrderCancelStatus.Pending);
    }

    [Test]
    public async Task AnotherUsersBotIsNotFound()
    {
        var missing = await Should.ThrowAsync<ResourceNotFoundException>(() => Handle(Guid.NewGuid(), engineOrderId: 7));

        missing.Code.ShouldBe("bot_not_found");
        await _cancels.DidNotReceive().TryAdd(Arg.Any<OrderCancelRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AStoppingBotWhoseRunnerStillHoldsTheLeaseRefuses()
    {
        Bot stopping = BotFixture.Running();
        stopping.Stop(Now);
        UseBot(stopping, leaseHeld: true);

        var refused = await Should.ThrowAsync<BotRuleException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 7));

        refused.Code.ShouldBe(BotRuleException.NotRunning);
        await _cancels.DidNotReceive().TryAdd(Arg.Any<OrderCancelRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AStoppedBotsSentOrderGetsACancelIntentAndARequestedCancel()
    {
        UseStoppedBot();
        Checkpoint(OrderStatus.Active);
        Submitted(sent: true);

        OrderCancelInternal cancel = await Handle(_bot.OwnerId.Value, engineOrderId: 7);

        cancel.ShouldSatisfyAllConditions(
            () => cancel.Status.ShouldBe(OrderCancelStatus.Requested),
            () => cancel.EngineOrderId.ShouldBe(7),
            () => cancel.ResolvedAt.ShouldBe(Now));
        await _intents.Received(1)
            .TryAdd(
                Arg.Is<OrderIntent>(intent => intent!.Kind == OrderIntentKind.Cancel && intent.EngineOrderId == 7 &&
                    intent.ClientOrderId == new ClientOrderId("pg-test-7") && !intent.FromKill && intent.Status == OrderIntentStatus.Pending),
                Arg.Any<CancellationToken>());
        await _cancels.Received(1)
            .TryAdd(
                Arg.Is<OrderCancelRequest>(request => request!.Status == OrderCancelStatus.Requested &&
                    request.DomainEvents.OfType<OrderCancelRequested>().Single().EngineOrderId == 7),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AStoppedBotsDeferredSubmitIsCancelledThroughAnIntent()
    {
        UseStoppedBot();
        OrderIntent deferred = Submitted(sent: false);
        deferred.Claim(Now);
        deferred.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now);

        (await Handle(_bot.OwnerId.Value, engineOrderId: 7)).Status.ShouldBe(OrderCancelStatus.Requested);
        await _intents.Received(1).TryAdd(Arg.Is<OrderIntent>(intent => intent!.Kind == OrderIntentKind.Cancel), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AStoppedBotsSubmitThatNeverLeftRefuses()
    {
        UseStoppedBot();
        Checkpoint(OrderStatus.PendingSubmit);
        Submitted(sent: false);

        (await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 7))).Code.ShouldBe("order_not_sent");
        await _intents.DidNotReceive().TryAdd(Arg.Any<OrderIntent>(), Arg.Any<CancellationToken>());
        await _cancels.DidNotReceive().TryAdd(Arg.Any<OrderCancelRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AStoppedBotsOrderWithACancelAlreadyRefuses()
    {
        UseStoppedBot();
        OrderIntent submit = Submitted(sent: true);
        _intents.ListByBot(_bot.Id, Arg.Any<CancellationToken>())
            .Returns([submit, OrderIntent.Cancel(OrderIntentId.New(), _bot.Id, engineOrderId: 7, submit.ClientOrderId, fromKill: true, Now)]);

        (await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 7))).Code.ShouldBe("order_cancel_pending");
        await _intents.DidNotReceive().TryAdd(Arg.Any<OrderIntent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AStoppedBotWithAnExpiredLeaseTakesTheCancel()
    {
        Bot stopped = BotFixture.Running();
        stopped.Stop(Now);
        UseBot(stopped, leaseHeld: false);
        Checkpoint(OrderStatus.Active);
        Submitted(sent: true);

        (await Handle(_bot.OwnerId.Value, engineOrderId: 7)).Status.ShouldBe(OrderCancelStatus.Requested);
    }

    [Test]
    public async Task AnOrderTheBotDoesNotHoldRefuses()
    {
        (await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 8))).Code.ShouldBe("order_not_open");

        Checkpoint(OrderStatus.Filled);

        (await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 7))).Code.ShouldBe("order_not_open");
        await _cancels.DidNotReceive().TryAdd(Arg.Any<OrderCancelRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ABotWithoutARunRefuses()
    {
        _checkpoints.Get(_bot.Id, Arg.Any<CancellationToken>()).Returns((BotCheckpoint?)null);

        (await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 7))).Code.ShouldBe("order_not_open");
    }

    [Test]
    public async Task AnOrderAlreadyBeingCancelledRefuses()
    {
        Checkpoint(OrderStatus.PendingCancel);

        (await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 7))).Code.ShouldBe("order_cancel_pending");
    }

    [Test]
    public async Task ABotHasOnePendingCancel()
    {
        _cancels.TryAdd(Arg.Any<OrderCancelRequest>(), Arg.Any<CancellationToken>()).Returns(false);

        (await Should.ThrowAsync<StateConflictException>(() => Handle(_bot.OwnerId.Value, engineOrderId: 7))).Code.ShouldBe("order_cancel_request_pending");
    }

    [Test]
    public async Task AnUntakenCancelExpiresBeforeTheNextIsStored()
    {
        var stale = OrderCancelRequest.Create(OrderCancelRequestId.New(), _bot.Id, engineOrderId: 7, Now - TimeSpan.FromMinutes(2));
        _cancels.GetPending(_bot.Id, Arg.Any<CancellationToken>()).Returns(stale);
        _cancels.GetForUpdate(stale.Id, Arg.Any<CancellationToken>()).Returns(stale);

        await Handle(_bot.OwnerId.Value, engineOrderId: 7);

        stale.Status.ShouldBe(OrderCancelStatus.Rejected);
        stale.RejectionReason.ShouldBe("expired");
        await _cancels.Received(1).Update(stale, Arg.Any<CancellationToken>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ANonPositiveEngineOrderIsInvalid(long engineOrderId)
    {
        new CancelBotOrderCommandValidator().Validate(new CancelBotOrderCommand(Guid.NewGuid(), Guid.NewGuid(), engineOrderId)).IsValid.ShouldBeFalse();
        new CancelBotOrderCommandValidator().Validate(new CancelBotOrderCommand(Guid.NewGuid(), Guid.NewGuid(), EngineOrderId: 1)).IsValid.ShouldBeTrue();
    }

    [Test]
    public async Task TheOwnerReadsTheCancelAndAnUntakenOneReadsAsExpired()
    {
        var request = OrderCancelRequest.Create(OrderCancelRequestId.New(), _bot.Id, engineOrderId: 7, Now);
        _cancels.Get(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var handler = new GetOrderCancelQueryHandler(_bots, _cancels, new FakeTimeProvider(Now + OrderCancelRequest.Lifetime + TimeSpan.FromSeconds(1)));

        OrderCancelInternal read = await handler.Handle(new GetOrderCancelQuery(_bot.OwnerId.Value, _bot.Id.Value, request.Id.Value), CancellationToken.None);

        read.Status.ShouldBe(OrderCancelStatus.Rejected);
        read.RejectionReason.ShouldBe("expired");
        (await Should.ThrowAsync<ResourceNotFoundException>(() => handler.Handle(
            new GetOrderCancelQuery(_bot.OwnerId.Value, _bot.Id.Value, Guid.NewGuid()),
            CancellationToken.None))).Code.ShouldBe("order_cancel_not_found");
        await Should.ThrowAsync<ResourceNotFoundException>(() => handler.Handle(
            new GetOrderCancelQuery(Guid.NewGuid(), _bot.Id.Value, request.Id.Value),
            CancellationToken.None));
    }

    [Test]
    public async Task TheLeaseHolderRejectsAnExpiredCancelAndAStaleHolderDoesNot()
    {
        var holder = LeaseHolderId.New();
        BotLease lease = BotLease.Acquire(_bot.Id, holder, Now, TimeSpan.FromMinutes(1));
        var request = OrderCancelRequest.Create(OrderCancelRequestId.New(), _bot.Id, engineOrderId: 7, Now);
        var leases = Substitute.For<IBotLeaseRepository>();
        leases.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns(lease);
        _cancels.GetForUpdate(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var handler = new ResolveOrderCancelCommandHandler(leases, _cancels, new FakeTimeProvider(Now));

        (await handler.Handle(new ResolveOrderCancelCommand(_bot.Id.Value, Guid.NewGuid(), lease.Epoch, request.Id.Value, "expired"), CancellationToken.None))
            .ShouldBeFalse();
        request.Status.ShouldBe(OrderCancelStatus.Pending);
        (await handler.Handle(new ResolveOrderCancelCommand(_bot.Id.Value, holder.Value, lease.Epoch, request.Id.Value, "expired"), CancellationToken.None))
            .ShouldBeTrue();
        request.RejectionReason.ShouldBe("expired");
    }

    [Test]
    public async Task TheFeedRecordsTheOwnersCancel()
    {
        var feed = new RecordingFeed();

        await new RecordBotFeedHandler(feed).Handle(new OrderCancelRequested(_bot.Id, OrderCancelRequestId.New(), EngineOrderId: 7, Now), CancellationToken.None);

        BotFeedEntry entry = feed.Entries.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(BotFeedKind.OrderCancelRequested);
        entry.OccurredAt.ShouldBe(Now);
        entry.Details.ShouldBe(new Dictionary<string, string> { ["engineOrderId"] = "7" });
    }

    private Task<OrderCancelInternal> Handle(Guid owner, long engineOrderId)
    {
        var handler = new CancelBotOrderCommandHandler(_bots, _leases, _checkpoints, _cancels, _intents, new FakeTimeProvider(Now));

        return handler.Handle(new CancelBotOrderCommand(owner, _bot.Id.Value, engineOrderId), CancellationToken.None);
    }

    private void UseBot(Bot bot, bool leaseHeld)
    {
        _bot = bot;
        _bots.Get(bot.Id, Arg.Any<CancellationToken>()).Returns(bot);
        _bots.GetForUpdate(bot.Id, Arg.Any<CancellationToken>()).Returns(bot);
        BotLease lease = BotLease.Acquire(bot.Id, LeaseHolderId.New(), leaseHeld ? Now : Now.AddMinutes(-10), TimeSpan.FromMinutes(1));
        _leases.GetForUpdate(bot.Id, Arg.Any<CancellationToken>()).Returns(lease);
    }

    private void UseStoppedBot()
    {
        UseBot(BotFixture.Stopped(), leaseHeld: false);
        _leases.GetForUpdate(_bot.Id, Arg.Any<CancellationToken>()).Returns((BotLease?)null);
        Checkpoint(OrderStatus.Active);
    }

    /// <summary>
    ///     The submit of order 7: sent to the venue, or still waiting in the process.
    /// </summary>
    private OrderIntent Submitted(bool sent)
    {
        OrderIntent submit = OrderIntent.Submit(OrderIntentId.New(), _bot.Id, _order, new ClientOrderId("pg-test-7"), Now);

        if (sent)
        {
            submit.Claim(Now);
            submit.MarkPlaceStarted(Now);
            submit.MarkSent(Now);
        }

        _intents.ListByBot(_bot.Id, Arg.Any<CancellationToken>()).Returns([submit]);

        return submit;
    }

    private void Checkpoint(OrderStatus status)
    {
        _order = new OrderSnapshot(
            Id: 7,
            "ladder.exit",
            OrderSide.Sell,
            OrderType.Limit,
            Quantity: 0.1m,
            LimitPrice: 110m,
            QuoteBudget: null,
            Reservation: 0.1m,
            "exit",
            EligibleFromCandleIndex: 0,
            status);
        var account = new ExternalAccountCheckpoint(
            StartingCapital: 1000m,
            QuoteTotal: 990m,
            QuoteReserved: 0m,
            BaseTotal: 0.1m,
            BaseReserved: 0.1m,
            CostBasis: 10m,
            RealizedPnl: 0m,
            FeesBase: 0m,
            FeesQuote: 0m,
            LastBuyPrice: 100m,
            NextOrderId: 8,
            [_order],
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
        _checkpoints.Get(_bot.Id, Arg.Any<CancellationToken>()).Returns(new BotCheckpoint(_bot.Id, engine, _bot.Parameters, Now));
    }
}