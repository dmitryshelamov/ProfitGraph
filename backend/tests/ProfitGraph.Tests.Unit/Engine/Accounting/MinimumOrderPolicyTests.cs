using System.Globalization;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

public sealed class MinimumOrderPolicyTests
{
    [TestCase("20", "10", "2", "20")]
    [TestCase("2", "10", "0.5", "5")]
    [TestCase("5", "3", "1.7", "5.1")]
    [TestCase("0.1", "10", "0.5", "5")]
    public void LimitBuyFloorsThenRaisesOnlyForMinimumNotional(string amount, string price, string quantity, string quote)
    {
        OrderSizingResult result = Size(OrderType.Limit, Number(amount), Number(price));
        result.Reason.ShouldBeNull();
        result.Quantity.ShouldBe(Number(quantity));
        result.Reservation.ShouldBe(Number(quote));
    }

    [Test]
    public void MinimumQuantityAloneIsNotRaised()
    {
        OrderSizingResult result = Size(OrderType.Limit, amount: 6m, price: 10m, minimumQuantity: 1m);
        result.Quantity.ShouldBe(0.6m);
        result.Reason.ShouldBe(RejectionReason.BelowMinQuantity);
    }

    [Test]
    public void RaiseDoesNotEraseAnInsufficientMinimumQuantity()
    {
        OrderSizingResult result = Size(OrderType.Limit, amount: 2m, price: 10m, minimumQuantity: 1m);
        result.Quantity.ShouldBe(0.5m);
        result.Reason.ShouldBe(RejectionReason.BelowMinQuantity);
    }

    [TestCase("2", "4.99")]
    [TestCase("10.09", "10.01")]
    public void LimitBuyDoesNotClipNominalOrRaisedAmountToAvailableQuote(string amount, string available)
    {
        OrderSizingResult result = Size(OrderType.Limit, Number(amount), price: 10m, Number(available));
        result.Reason.ShouldBe(RejectionReason.InsufficientQuote);
    }

    [Test]
    public void DefaultPolicyStillRejectsTheSameBelowMinimumOrder()
    {
        OrderSizingResult result = Size(OrderType.Limit, amount: 2m, price: 10m, policy: MinimumOrderPolicy.Reject);
        result.Quantity.ShouldBe(0.2m);
        result.Reason.ShouldBe(RejectionReason.BelowMinNotional);
    }

    [TestCase("2", "5", "5")]
    [TestCase("7.129", "5", "7.12")]
    [TestCase("2", "5.001", "5.01")]
    public void MarketBuyRaisesOnlyItsRepresentableQuoteBudget(string amount, string minimumNotional, string quote)
    {
        OrderSizingResult result = Size(OrderType.Market, Number(amount), price: null, minimumNotional: Number(minimumNotional));
        result.Reason.ShouldBeNull();
        result.Quantity.ShouldBeNull();
        result.Reservation.ShouldBe(Number(quote));
    }

    [Test]
    public void MarketBuyCannotRaiseBeyondAvailableQuote()
    {
        OrderSizingResult result = Size(OrderType.Market, amount: 2m, price: null, available: 4.99m);
        result.Reason.ShouldBe(RejectionReason.InsufficientQuote);
    }

    [Test]
    public void MarketSubmissionDoesNotGuessFutureQuantityButReachedPricePreviewChecksIt()
    {
        Size(OrderType.Market, amount: 2m, price: null, minimumQuantity: 1m).Reason.ShouldBeNull();
        OrderSizingResult preview = Size(OrderType.Market, amount: 2m, price: null, minimumQuantity: 1m, referencePrice: 10m);
        preview.Quantity.ShouldBe(0.5m);
        preview.Reason.ShouldBe(RejectionReason.BelowMinQuantity);
    }

    [Test]
    public void SharedAccountReservesRaisedQuoteAndNeverMutatesOnRefusal()
    {
        InstrumentRules rules = Rules();
        var settings = new ExecutionSettings(startingCapital: 6m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var account = new TradingAccount(settings, rules);
        var request = new OrderRequest(
            OrderSide.Buy,
            OrderType.Limit,
            Amount: 2m,
            LimitPrice: 10m,
            Tag: null,
            "buy",
            EligibleFromCandleIndex: 0,
            MinimumOrderPolicy.RaiseToExchangeMinimum);
        account.Submit(request).Order.ShouldNotBeNull().Reservation.ShouldBe(5m);
        account.Submit(request).Rejection.ShouldNotBeNull().Reason.ShouldBe(RejectionReason.InsufficientQuote);
        account.Capture().QuoteReserved.ShouldBe(5m);
    }

    private static OrderSizingResult Size(
        OrderType type,
        decimal amount,
        decimal? price,
        decimal available = 100m,
        decimal minimumQuantity = 0.1m,
        decimal minimumNotional = 5m,
        MinimumOrderPolicy policy = MinimumOrderPolicy.RaiseToExchangeMinimum,
        decimal? referencePrice = null)
    {
        InstrumentRules rules = Rules(minimumQuantity, minimumNotional);
        var settings = new ExecutionSettings(available, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var account = new TradingAccount(settings, rules);
        var request = new OrderRequest(OrderSide.Buy, type, amount, price, Tag: null, "buy", EligibleFromCandleIndex: 0, policy);

        return new OrderSizingService().Resolve(request, rules, settings, account.Capture(), referencePrice);
    }

    private static InstrumentRules Rules(decimal minimumQuantity = 0.1m, decimal minimumNotional = 5m)
    {
        return new InstrumentRules(quantityStep: 0.1m, priceTick: 0.1m, minimumQuantity, minimumNotional, basePrecision: 2, quotePrecision: 2);
    }

    private static decimal Number(string text)
    {
        return decimal.Parse(text, CultureInfo.InvariantCulture);
    }
}