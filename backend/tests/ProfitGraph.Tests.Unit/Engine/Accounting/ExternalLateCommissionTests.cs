using System.Collections.Immutable;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

public sealed class ExternalLateCommissionTests
{
    private static readonly ExecutionSettings Settings = new ExecutionSettings(startingCapital: 10000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 10m,
        priceTick: 0.01m,
        minQuantity: 10m,
        minNotional: 0m,
        basePrecision: 0,
        quotePrecision: 2,
        FeeAsset.Quote,
        exactQuoteFees: true);

    [Test]
    public void TerminalFeeCorrectionIsAppliedOnceWithoutAnotherFillAndSurvivesRestart()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 20m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 20m, grossQuote: 200m, fee: 0m), rejectionAttemptId: 0).Emissions.Count.ShouldBe(1);
        account = ExternallyExecutedAccount.Restore(account.CaptureCheckpoint(), Settings, Rules);

        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 1.50m), rejectionAttemptId: 0)
            .Emissions.ShouldBeEmpty();
        account.Capture().QuoteTotal.ShouldBe(9798.50m);
        account.Capture().CostBasis.ShouldBe(201.50m);
        account.Capture().FeesQuote.ShouldBe(1.50m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 1.50m), rejectionAttemptId: 0)
            .DiagnosticCode.ShouldBe("DuplicateFeeCorrection");
        account.Capture().FeesQuote.ShouldBe(1.50m);
    }

    [Test]
    public void BuyFeeAfterPartialSaleSplitsRemainingCostAndRealizedProfit()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 20m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 20m, grossQuote: 200m, fee: 0m), rejectionAttemptId: 0);
        Sell(account, quantity: 10m, quote: 150m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 2m), rejectionAttemptId: 0);

        account.Capture().CostBasis.ShouldBe(101m);
        account.Capture().RealizedPnl.ShouldBe(49m);
        account.Capture().FeesQuote.ShouldBe(2m);
        Sell(account, quantity: 10m, quote: 150m);
        account.Capture().RealizedPnl.ShouldBe(98m);
        account.Capture().CostBasis.ShouldBe(0m);
    }

    [Test]
    public void FeeForClosedCycleDoesNotChangeNewPositionsCostBasis()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long first = Buy(account, quantity: 10m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(first, grossBase: 10m, grossQuote: 100m, fee: 0m), rejectionAttemptId: 0);
        Sell(account, quantity: 10m, quote: 150m);
        long second = Buy(account, quantity: 10m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(second, grossBase: 10m, grossQuote: 100m, fee: 0m), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(first, grossBase: 10m, grossQuote: 100m, cumulativeFee: 1.50m), rejectionAttemptId: 0);

        account.Capture().CostBasis.ShouldBe(100m);
        account.Capture().RealizedPnl.ShouldBe(48.50m);
        account.Capture().BaseTotal.ShouldBe(10m);
    }

    [Test]
    public void PartialCancelledOrderRetainsReceiptAndRejectsDecreasingOrForeignFees()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 20m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(buy, grossBase: 10m, grossQuote: 100m, fee: 0m), rejectionAttemptId: 0);
        account.RequestCancel(buy);
        account.ApplyExternalUpdate(ExternalOrderUpdate.ConfirmCancel(buy), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 10m, grossQuote: 100m, cumulativeFee: 1m), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 10m, grossQuote: 100m, cumulativeFee: 0m), rejectionAttemptId: 0)
            .DiagnosticCode.ShouldBe("InvalidFeeCorrection");
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(orderId: 999, grossBase: 10m, grossQuote: 100m, cumulativeFee: 1m), rejectionAttemptId: 0)
            .DiagnosticCode.ShouldBe("InvalidFeeCorrection");
        account.Capture().FeesQuote.ShouldBe(1m);
    }

    [Test]
    public void PartialFeesCanArriveBetweenExecutionsAndAfterTerminalFill()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 20m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(buy, grossBase: 10m, grossQuote: 100m, fee: 0m), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 10m, grossQuote: 100m, cumulativeFee: 1.50m), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 20m, grossQuote: 200m, fee: 1.50m), rejectionAttemptId: 0)
            .Emissions.ShouldHaveSingleItem();
        account.ApplyExternalUpdate(
            ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 2.50m, commissionPartitioned: true),
            rejectionAttemptId: 0);
        account.Capture().FeesQuote.ShouldBe(2.50m);
        account.Capture().CostBasis.ShouldBe(202.50m);
        account.Capture().BaseTotal.ShouldBe(20m);
    }

    [Test]
    public void SellFeeCorrectionReducesRealizedProfitWithoutChangingShares()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 10m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 10m, grossQuote: 100m, fee: 0m), rejectionAttemptId: 0);
        Sell(account, quantity: 10m, quote: 150m);
        long sell = account.CaptureCheckpoint().FeeReceipts.Single(receipt => receipt.Side == OrderSide.Sell).OrderId;
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(sell, grossBase: 10m, grossQuote: 150m, cumulativeFee: 1.50m), rejectionAttemptId: 0);
        account.Capture().RealizedPnl.ShouldBe(48.50m);
        account.Capture().BaseTotal.ShouldBe(0m);
        account.Capture().CostBasis.ShouldBe(0m);
    }

    [Test]
    public void PendingCommissionReservesConfiguredFeeForFollowingOrdersAndClearsOnConfirmedZero()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 10m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 10m, grossQuote: 100m, fee: 0m, commissionPending: true), rejectionAttemptId: 0);
        account.Capture().QuoteAvailable.ShouldBe(9899.9m);
        account = ExternallyExecutedAccount.Restore(account.CaptureCheckpoint(), Settings, Rules);
        var following = new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 9890.1m, LimitPrice: 989.01m, Tag: null, "buy", EligibleFromCandleIndex: 0);
        account.Submit(following).Rejection.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 10m, grossQuote: 100m, cumulativeFee: 0m), rejectionAttemptId: 0);
        account.Capture().QuoteAvailable.ShouldBe(9900m);
        account.Submit(following).Order.ShouldNotBeNull();
    }

    [Test]
    public void FeeAboveEstimatePreservesTruthWhenAnotherOrderAlreadyReservedTheCash()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 10m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 10m, grossQuote: 100m, fee: 0m, commissionPending: true), rejectionAttemptId: 0);
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 9890m, LimitPrice: 989m, Tag: null, "following", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 10m, grossQuote: 100m, cumulativeFee: 25m), rejectionAttemptId: 0);
        account.Capture().QuoteTotal.ShouldBe(9875m);
        account.Capture().QuoteReserved.ShouldBe(9890m);
        account.Capture().QuoteAvailable.ShouldBe(-15m);
        account.Capture().FeesQuote.ShouldBe(25m);
        account = ExternallyExecutedAccount.Restore(account.CaptureCheckpoint(), Settings, Rules);
        account.Capture().FeesQuote.ShouldBe(25m);
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 100m, LimitPrice: 10m, Tag: null, "next", EligibleFromCandleIndex: 0))
            .Rejection.ShouldNotBeNull();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SoldOldBatchFeeAndNewBatchFeeStaySeparateAcrossRestart(bool restart)
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 20m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(buy, grossBase: 10m, grossQuote: 100m, fee: 0m, commissionPending: true), rejectionAttemptId: 0);
        Sell(account, quantity: 10m, quote: 150m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 20m, grossQuote: 200m, fee: 4m), rejectionAttemptId: 0);
        account.Capture().FeesQuote.ShouldBe(0m);

        if (restart)
        {
            account = ExternallyExecutedAccount.Restore(account.CaptureCheckpoint(), Settings, Rules);
        }

        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 4m), rejectionAttemptId: 0)
            .DiagnosticCode.ShouldBe("UnpartitionedFeeCorrection");
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 10m, grossQuote: 100m, cumulativeFee: 1m), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(
                ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 4m, commissionPartitioned: true),
                rejectionAttemptId: 0)
            .Emissions.ShouldBeEmpty();
        account.Capture().CostBasis.ShouldBe(103m);
        account.Capture().RealizedPnl.ShouldBe(49m);
        account.Capture().FeesQuote.ShouldBe(4m);
        account.ApplyExternalUpdate(
                ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 4m, commissionPartitioned: true),
                rejectionAttemptId: 0)
            .DiagnosticCode.ShouldBe("DuplicateFeeCorrection");
    }

    [Test]
    public void PendingReserveBelongsToUnknownBatchAndConfirmedZeroClearsIt()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 20m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(buy, grossBase: 10m, grossQuote: 100m, fee: 5m), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 20m, grossQuote: 200m, fee: 5m, commissionPending: true), rejectionAttemptId: 0);
        account.CaptureCheckpoint().FeeReceipts.Single().PendingFeeReserve.ShouldBe(0.1m);
        account.ApplyExternalUpdate(
            ExternalOrderUpdate.CorrectFee(buy, grossBase: 20m, grossQuote: 200m, cumulativeFee: 5m, commissionPartitioned: true),
            rejectionAttemptId: 0);
        account.CaptureCheckpoint().FeeReceipts.Single().PendingFeeReserve.ShouldBe(0m);
        account.Capture().FeesQuote.ShouldBe(5m);
    }

    [Test]
    public void RawReceiptWithoutSlicesDoesNotAssumeAggregateFeeProvesOldBatch()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        long buy = Buy(account, quantity: 20m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(buy, grossBase: 10m, grossQuote: 100m, fee: 0m, commissionPending: true), rejectionAttemptId: 0);
        Sell(account, quantity: 10m, quote: 150m);
        ExternalAccountCheckpoint checkpoint = account.CaptureCheckpoint();
        checkpoint = checkpoint with { FeeReceipts = ImmutableArray.CreateRange(checkpoint.FeeReceipts.Select(receipt => receipt with { Slices = null })) };
        account = ExternallyExecutedAccount.Restore(checkpoint, Settings, Rules);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 20m, grossQuote: 200m, fee: 4m, commissionPartitioned: true), rejectionAttemptId: 0);
        account.Capture().FeesQuote.ShouldBe(0m);
        account.Capture().CostBasis.ShouldBe(100m);
        account.Capture().RealizedPnl.ShouldBe(50m);
        account.CaptureCheckpoint().FeeReceipts.Single(receipt => receipt.OrderId == buy).PendingFeeReserve.ShouldBe(0.2m);
    }

    [Test]
    public void BybitCannotUseQuoteFeeCorrectionAndKeepsEmptyReceipts()
    {
        var account = new ExternallyExecutedAccount(
            Settings,
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 2, quotePrecision: 2));
        long buy = Buy(account, quantity: 10m);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(buy, grossBase: 10m, grossQuote: 100m, fee: 0m), rejectionAttemptId: 0);
        account.CaptureCheckpoint().FeeReceipts.IsDefaultOrEmpty.ShouldBeTrue();
        account.ApplyExternalUpdate(ExternalOrderUpdate.CorrectFee(buy, grossBase: 10m, grossQuote: 100m, cumulativeFee: 1m), rejectionAttemptId: 0)
            .DiagnosticCode.ShouldBe("InvalidFeeCorrection");
        account.Capture().FeesQuote.ShouldBe(0m);
    }

    private static long Buy(ExternallyExecutedAccount account, decimal quantity)
    {
        Order order = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, quantity * 10m, LimitPrice: 10m, Tag: null, "buy", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.Accept(order.Id), rejectionAttemptId: 0);

        return order.Id;
    }

    private static void Sell(ExternallyExecutedAccount account, decimal quantity, decimal quote)
    {
        Order order = account.Submit(new OrderRequest(OrderSide.Sell, OrderType.Limit, quantity, quote / quantity, Tag: null, "sell", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        account.ApplyExternalUpdate(ExternalOrderUpdate.Accept(order.Id), rejectionAttemptId: 0);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(order.Id, quantity, quote, fee: 0m), rejectionAttemptId: 0);
    }
}