using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class ExternalOrderScenarioTests
{
    [Test]
    public void PendingSubmissionOccupiesItsTagWithoutPublishingSubmitted()
    {
        var harness = new ExternalOrderHarness(twoBuys: true);
        DispatchBatch started = harness.Start();
        Kinds(started).ShouldBe(new[] { EventKind.Start, EventKind.Rejected });
        started.Occurrences[1].Payload.ShouldBeOfType<RejectionPayload>().Rejection.Reason.ShouldBe(RejectionReason.TagActive);
        AccountSnapshot pending = harness.Account.Capture();
        pending.Orders.Single().Status.ShouldBe(OrderStatus.PendingSubmit);
        pending.QuoteReserved.ShouldBe(25m);

        DispatchBatch accepted = harness.Update(ExternalOrderUpdate.Accept(orderId: 1));
        Kinds(accepted).ShouldBe(new[] { EventKind.Submitted });
        accepted.Occurrences[0].Payload.ShouldBeOfType<OrderPayload>().Order.Status.ShouldBe(OrderStatus.Active);
        accepted.Occurrences[0].Phase.ShouldBe(PricePhase.External);
        accepted.Occurrences[0].CausationId.ShouldBeNull();
        harness.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.Active);
    }

    [Test]
    public void RejectedPendingSubmissionReleasesReservationAndOriginTag()
    {
        var harness = new ExternalOrderHarness();
        Kinds(harness.Start()).ShouldBe(new[] { EventKind.Start });

        DispatchBatch rejected = harness.Update(ExternalOrderUpdate.Reject(orderId: 1, RejectionReason.InvalidAmount, "Exchange rejected the order."));
        Kinds(rejected).ShouldBe(new[] { EventKind.Rejected });
        var payload = rejected.Occurrences[0].Payload.ShouldBeOfType<RejectionPayload>();
        payload.Rejection.OriginNodeId.ShouldBe("buy");
        payload.Rejection.OrderId.ShouldBe(1);
        harness.Account.Capture().Orders.ShouldBeEmpty();
        harness.Account.Capture().QuoteReserved.ShouldBe(0m);
        harness.Account.Capture().QuoteAvailable.ShouldBe(250m);
        harness.Account.Submit(harness.Account.Submissions[0]).Order.ShouldNotBeNull().Status.ShouldBe(OrderStatus.PendingSubmit);
    }

    [Test]
    public void OwnerConfirmedAbsenceAfterPartialFillIsStaleAndPreservesTheActiveOrder()
    {
        var harness = new ExternalOrderHarness();
        harness.Start();
        harness.Update(ExternalOrderUpdate.Accept(orderId: 1));
        harness.Update(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 2m, grossQuote: 10m, fee: 0.02m));
        Kinds(harness.Update(ExternalOrderUpdate.Reject(orderId: 1, RejectionReason.InvalidAmount, "stale exchange rejection"))).ShouldBeEmpty();

        DispatchBatch confirmed = harness.Update(
            ExternalOrderUpdate.Reject(
                orderId: 1,
                RejectionReason.OwnerConfirmedAbsent,
                "Owner confirmed the order is absent in Bybit.",
                ownerConfirmedAbsent: true));
        Kinds(confirmed).ShouldBeEmpty();
        AccountSnapshot snapshot = harness.Account.Capture();
        snapshot.Orders.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.Active);
        snapshot.QuoteReserved.ShouldBe(15m);
        snapshot.QuoteTotal.ShouldBe(240m);
        snapshot.BaseTotal.ShouldBe(1.98m);
    }

    [Test]
    public void TwoPartialsThenFinalPublishOneAggregateFillAndExactBalances()
    {
        var harness = new ExternalOrderHarness();
        harness.Start();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(orderId: 1))).ShouldBe(new[] { EventKind.Submitted });
        Kinds(harness.Update(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 1m, grossQuote: 4.8m, fee: 0.01m), price: 4.8m)).ShouldBeEmpty();
        Kinds(harness.Update(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 3m, grossQuote: 14.6m, fee: 0.03m), price: 4.9m)).ShouldBeEmpty();
        DispatchBatch final = harness.Update(ExternalOrderUpdate.Final(orderId: 1, grossBase: 5m, grossQuote: 24.6m, fee: 0.05m));
        Kinds(final).ShouldBe(new[] { EventKind.Filled });
        Fill fill = final.Occurrences[0].Payload.ShouldBeOfType<FillPayload>().Fill;
        fill.GrossBase.ShouldBe(5m);
        fill.Price.ShouldBe(4.92m);
        fill.Fee.ShouldBe(0.05m);
        fill.FeeAsset.ShouldBe(FeeAsset.Base);
        fill.BaseChange.ShouldBe(4.95m);
        fill.QuoteChange.ShouldBe(-24.6m);
        fill.RealizedPnl.ShouldBe(0m);
        AccountSnapshot account = harness.Account.Capture();
        account.Orders.ShouldBeEmpty();
        account.QuoteTotal.ShouldBe(225.4m);
        account.QuoteReserved.ShouldBe(0m);
        account.BaseTotal.ShouldBe(4.95m);
        account.CostBasis.ShouldBe(24.6m);
        account.FeesBase.ShouldBe(0.05m);
    }

    [Test]
    public void PartialSellAggregatesQuoteFeesAndRealizedPnl()
    {
        var settings = new ExecutionSettings(startingCapital: 100m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var rules = new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 2, quotePrecision: 2);
        var account = new ExternallyExecutedAccount(settings, rules);
        Order buy = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 25m, LimitPrice: null, "entry", "buy", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.Accept(buy.Id), rejectionAttemptId: 1).Emissions.Single().Kind.ShouldBe(EventKind.Submitted);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy.Id, grossBase: 5m, grossQuote: 25m, fee: 0.05m), rejectionAttemptId: 1)
            .Emissions.Single()
            .Kind.ShouldBe(EventKind.Filled);
        Order sell = account.Submit(new OrderRequest(OrderSide.Sell, OrderType.Limit, Amount: 4m, LimitPrice: 6m, "exit", "sell", EligibleFromCandleIndex: 1))
            .Order.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.Accept(sell.Id), rejectionAttemptId: 2).Emissions.Single().Kind.ShouldBe(EventKind.Submitted);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(sell.Id, grossBase: 1m, grossQuote: 6m, fee: 0.06m), rejectionAttemptId: 2)
            .Emissions.ShouldBeEmpty();
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(sell.Id, grossBase: 2m, grossQuote: 12.5m, fee: 0.12m), rejectionAttemptId: 2)
            .Emissions.ShouldBeEmpty();
        ExternalOrderUpdateResult completed = account.ApplyExternalUpdate(
            ExternalOrderUpdate.Final(sell.Id, grossBase: 4m, grossQuote: 25m, fee: 0.25m),
            rejectionAttemptId: 2);
        Fill fill = completed.Emissions.Single().Payload.ShouldBeOfType<FillPayload>().Fill;
        fill.Price.ShouldBe(6.25m);
        fill.GrossBase.ShouldBe(4m);
        fill.BaseChange.ShouldBe(-4m);
        fill.QuoteChange.ShouldBe(24.75m);
        fill.Fee.ShouldBe(0.25m);
        fill.FeeAsset.ShouldBe(FeeAsset.Quote);
        AccountSnapshot snapshot = account.Capture();
        fill.RealizedPnl.ShouldBe(snapshot.RealizedPnl);
        snapshot.FeesQuote.ShouldBe(0.25m);
        snapshot.QuoteTotal.ShouldBe(99.75m);
        snapshot.BaseTotal.ShouldBe(0.95m);
        snapshot.BaseReserved.ShouldBe(0m);
    }

    [Test]
    public void ConfirmedCancelPublishesCancelledBeforeCompletionAndRepeatedRequestIsSilent()
    {
        var harness = new ExternalOrderHarness(cancelOnSubmitted: true, duplicateCancel: true);
        harness.Start();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(orderId: 1))).ShouldBe(new[] { EventKind.Submitted });
        harness.Account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingCancel);
        harness.Account.Capture().QuoteReserved.ShouldBe(25m);

        DispatchBatch cancelled = harness.Update(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        Kinds(cancelled).ShouldBe(new[] { EventKind.Cancelled, EventKind.CancelCompleted });
        var completed = cancelled.Occurrences[1].Payload.ShouldBeOfType<CancelResultPayload>();
        completed.Cancelled.ShouldBeTrue();
        completed.CancelledOrderId.ShouldBe(1);
        completed.OriginNodeId.ShouldBe("cancel");
        harness.Account.Capture().Orders.ShouldBeEmpty();
        harness.Account.Capture().QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public void FinalFillRacingPendingCancelPublishesFillThenUnsuccessfulCompletion()
    {
        var harness = new ExternalOrderHarness(cancelOnSubmitted: true);
        harness.Start();
        harness.Update(ExternalOrderUpdate.Accept(orderId: 1));
        DispatchBatch filled = harness.Update(ExternalOrderUpdate.Final(orderId: 1, grossBase: 5m, grossQuote: 25m, fee: 0.05m));
        Kinds(filled).ShouldBe(new[] { EventKind.Filled, EventKind.CancelCompleted });
        filled.Occurrences[1].Payload.ShouldBeOfType<CancelResultPayload>().Cancelled.ShouldBeFalse();
        AccountSnapshot account = harness.Account.Capture();
        account.Orders.ShouldBeEmpty();
        account.BaseTotal.ShouldBe(4.95m);
        account.QuoteTotal.ShouldBe(225m);
        account.QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public void CancelAfterPartialKeepsFilledPositionAndPublishesCompletion()
    {
        var harness = new ExternalOrderHarness(cancelOnSubmitted: true);
        harness.Start();
        harness.Update(ExternalOrderUpdate.Accept(orderId: 1));
        Kinds(harness.Update(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 2m, grossQuote: 10m, fee: 0.02m))).ShouldBeEmpty();
        DispatchBatch cancelled = harness.Update(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        Kinds(cancelled).ShouldBe(new[] { EventKind.Cancelled, EventKind.CancelCompleted });
        cancelled.Occurrences[1].Payload.ShouldBeOfType<CancelResultPayload>().Cancelled.ShouldBeTrue();
        AccountSnapshot account = harness.Account.Capture();
        account.Orders.ShouldBeEmpty();
        account.BaseTotal.ShouldBe(1.98m);
        account.QuoteTotal.ShouldBe(240m);
        account.CostBasis.ShouldBe(10m);
        account.FeesBase.ShouldBe(0.02m);
        account.QuoteReserved.ShouldBe(0m);
    }

    [Test]
    public void StaleAndDuplicateUpdatesAreIgnoredWithDiagnostics()
    {
        var harness = new ExternalOrderHarness();
        harness.Start();
        harness.Update(ExternalOrderUpdate.Accept(orderId: 1));
        Kinds(harness.Update(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 2m, grossQuote: 10m, fee: 0.02m))).ShouldBeEmpty();
        Kinds(harness.Update(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 1m, grossQuote: 5m, fee: 0.01m))).ShouldBeEmpty();
        Kinds(harness.Update(ExternalOrderUpdate.Final(orderId: 1, grossBase: 5m, grossQuote: 25m, fee: 0.05m)))
            .ShouldBe(new[] { EventKind.Filled });
        Kinds(harness.Update(ExternalOrderUpdate.Final(orderId: 1, grossBase: 5m, grossQuote: 25m, fee: 0.05m))).ShouldBeEmpty();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(orderId: 999))).ShouldBeEmpty();
        harness.Dispatcher.ExternalOrderDiagnostics.Select(diagnostic => diagnostic.Code)
            .ShouldBe(new[] { "StaleOrInvalidFill", "TerminalOrder", "UnknownOrder" });
        harness.Account.Capture().Orders.ShouldBeEmpty();
        harness.Account.Capture().BaseTotal.ShouldBe(4.95m);
        harness.Account.Capture().QuoteTotal.ShouldBe(225m);
    }

    [Test]
    public void LadderCyclePreservesTagsAndPositionAcrossBothCancelRaces()
    {
        StrategyDefinition definition = LadderStrategyBlockFactory.Create(buyAmount: 25m, allocationCap: 75m, drop: 0.1m, rise: 0.1m);
        StrategyGraph graph = new StrategyDefinitionLowerer(Catalog).Lower(definition).Graph;
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
        var harness = new ExternalOrderHarness(compiled);
        var reference = new TradingAccount(harness.Account.Settings, harness.Account.Rules);

        Kinds(harness.Start()).ShouldBe(new[] { EventKind.Start });
        OrderSnapshot entry = Active(harness, "ladder.entry");
        entry.Status.ShouldBe(OrderStatus.PendingSubmit);
        Order referenceEntry = reference.Submit(harness.Account.Submissions[0]).Order.ShouldNotBeNull();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(entry.Id))).ShouldBe(new[] { EventKind.Submitted });
        Kinds(harness.Update(ExternalOrderUpdate.Final(entry.Id, grossBase: 5m, grossQuote: 25m, fee: 0m)))
            .ShouldBe(new[] { EventKind.Filled, EventKind.CancelCompleted });
        reference.Execute(referenceEntry.Id, referencePrice: 5m).Fill.ShouldNotBeNull();
        AssertUniqueTags(harness);

        OrderSnapshot firstExit = Active(harness, "ladder.exit");
        Order referenceFirstExit = reference.Submit(harness.Account.Submissions[1]).Order.ShouldNotBeNull();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(firstExit.Id))).ShouldBe(new[] { EventKind.Submitted });
        OrderSnapshot firstAverage = Active(harness, "ladder.average");
        Order referenceFirstAverage = reference.Submit(harness.Account.Submissions[2]).Order.ShouldNotBeNull();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(firstAverage.Id))).ShouldBe(new[] { EventKind.Submitted });
        decimal averageQuote = firstAverage.Quantity!.Value * firstAverage.LimitPrice!.Value;
        Kinds(harness.Update(ExternalOrderUpdate.Final(firstAverage.Id, firstAverage.Quantity.Value, averageQuote, fee: 0m), firstAverage.LimitPrice.Value))
            .ShouldBe(new[] { EventKind.Filled });
        reference.Execute(referenceFirstAverage.Id, firstAverage.LimitPrice.Value).Fill.ShouldNotBeNull();
        Active(harness, "ladder.exit").Status.ShouldBe(OrderStatus.PendingCancel);
        AssertUniqueTags(harness);

        decimal firstExitQuote = firstExit.Quantity!.Value * firstExit.LimitPrice!.Value;
        DispatchBatch racedExit = harness.Update(ExternalOrderUpdate.Final(firstExit.Id, firstExit.Quantity.Value, firstExitQuote, fee: 0m), firstExit.LimitPrice.Value);
        Kinds(racedExit).Take(2).ShouldBe(new[] { EventKind.Filled, EventKind.CancelCompleted });
        racedExit.Occurrences[1].Payload.ShouldBeOfType<CancelResultPayload>().Cancelled.ShouldBeFalse();
        reference.Execute(referenceFirstExit.Id, firstExit.LimitPrice.Value).Fill.ShouldNotBeNull();
        AssertUniqueTags(harness);

        OrderSnapshot secondExit = Active(harness, "ladder.exit");
        Order referenceSecondExit = reference.Submit(harness.Account.Submissions[3]).Order.ShouldNotBeNull();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(secondExit.Id))).ShouldBe(new[] { EventKind.Submitted });
        OrderSnapshot secondAverage = Active(harness, "ladder.average");
        Order referenceSecondAverage = reference.Submit(harness.Account.Submissions[4]).Order.ShouldNotBeNull();
        Kinds(harness.Update(ExternalOrderUpdate.Accept(secondAverage.Id))).ShouldBe(new[] { EventKind.Submitted });
        decimal secondExitQuote = secondExit.Quantity!.Value * secondExit.LimitPrice!.Value;
        Kinds(harness.Update(ExternalOrderUpdate.Final(secondExit.Id, secondExit.Quantity.Value, secondExitQuote, fee: 0m), secondExit.LimitPrice.Value))
            .ShouldBe(new[] { EventKind.Filled });
        reference.Execute(referenceSecondExit.Id, secondExit.LimitPrice.Value).Fill.ShouldNotBeNull();
        Active(harness, "ladder.average").Status.ShouldBe(OrderStatus.PendingCancel);
        AssertUniqueTags(harness);

        DispatchBatch cancelledAverage = harness.Update(ExternalOrderUpdate.ConfirmCancel(secondAverage.Id));
        Kinds(cancelledAverage).ShouldBe(new[] { EventKind.Cancelled, EventKind.CancelCompleted });
        cancelledAverage.Occurrences[1].Payload.ShouldBeOfType<CancelResultPayload>().Cancelled.ShouldBeTrue();
        reference.Cancel(referenceSecondAverage.Id).ShouldNotBeNull();
        reference.Submit(harness.Account.Submissions[5]).Order.ShouldNotBeNull();

        AccountSnapshot actual = harness.Account.Capture();
        AccountSnapshot expected = reference.Capture();
        actual.BaseTotal.ShouldBe(expected.BaseTotal);
        actual.BaseTotal.ShouldBe(0m);
        actual.CostBasis.ShouldBe(expected.CostBasis);
        actual.QuoteTotal.ShouldBe(expected.QuoteTotal);
        actual.QuoteReserved.ShouldBe(expected.QuoteReserved);
        actual.BaseReserved.ShouldBe(expected.BaseReserved);
        actual.RealizedPnl.ShouldBe(expected.RealizedPnl);
        actual.Orders.Select(order => order.Tag).ShouldBe(expected.Orders.Select(order => order.Tag));
        actual.Orders.Single().Tag.ShouldBe("ladder.entry");
        actual.Orders.Single().Status.ShouldBe(OrderStatus.PendingSubmit);
        actual.Orders.Single().EligibleFromCandleIndex.ShouldBe(1);
        harness.Account.EntriesWhilePositionOpen.ShouldBe(0);
        harness.Account.DuplicateTagsObserved.ShouldBeFalse();
        AssertUniqueTags(harness);
    }

    private static OrderSnapshot Active(ExternalOrderHarness harness, string tag)
    {
        return harness.Account.Capture().Orders.Single(order => order.Tag == tag);
    }

    private static void AssertUniqueTags(ExternalOrderHarness harness)
    {
        string[] tags = harness.Account.Capture().Orders.Where(order => order.Tag is not null).Select(order => order.Tag!).ToArray();
        tags.Distinct(StringComparer.Ordinal).Count().ShouldBe(tags.Length);
    }

    private static EventKind[] Kinds(DispatchBatch batch)
    {
        return batch.Occurrences.Select(occurrence => occurrence.Kind).ToArray();
    }
}