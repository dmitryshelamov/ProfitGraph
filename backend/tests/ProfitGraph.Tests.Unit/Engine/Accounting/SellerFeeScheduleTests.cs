using System.Globalization;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

/// <summary>
///     Invented rates and minimum from Profit Fox Designer ADR 0082: 5% and 10% with one minor unit (0.01) each.
/// </summary>
public sealed class SellerFeeScheduleTests
{
    private const int Precision = 2;

    private static readonly SellerFeeSchedule Schedule = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);

    private static readonly InstrumentRules WholeItems = new InstrumentRules(
        quantityStep: 1m,
        priceTick: 0.01m,
        minQuantity: 1m,
        minNotional: 0.03m,
        basePrecision: 0,
        Precision);

    [TestCase("0.01", "0.03")]
    [TestCase("0.19", "0.21")]
    [TestCase("0.20", "0.23")]
    [TestCase("1.00", "1.15")]
    [TestCase("0", "0")]
    public void AddsEveryComponentToTheSellerAmount(string seller, string buyer)
    {
        Schedule.BuyerPrice(Number(seller), Precision).ShouldBe(Number(buyer));
    }

    [TestCase("0.21", "0.19")]
    [TestCase("0.22", "0.19")]
    [TestCase("0.23", "0.20")]
    [TestCase("0.02", "0")]
    public void GivesTheSellerTheLargestAmountTheBuyerPriceCovers(string buyer, string seller)
    {
        Schedule.SellerAmount(Number(buyer), Precision).ShouldBe(Number(seller));
    }

    [TestCase("0.21", "0.21")]
    [TestCase("0.22", "0.23")]
    [TestCase("0.02", "0.03")]
    public void RaisesAnUnreachablePriceAndKeepsAReachableOne(string requested, string raised)
    {
        Schedule.RaiseToRepresentable(Number(requested), Precision).ShouldBe(Number(raised));
    }

    [Test]
    public void ChargesTheMinimumPerUnitNotOncePerOrder()
    {
        Schedule.Fee(quantity: 3m, buyerPrice: 0.03m, Precision).ShouldBe(0.06m);
    }

    [Test]
    public void SkipsAComponentWithAZeroRate()
    {
        new SellerFeeSchedule([0m, 0.10m], minimum: 0.01m).BuyerPrice(sellerAmount: 0.19m, Precision).ShouldBe(0.20m);
    }

    [Test]
    public void ComparesByValue()
    {
        new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m).ShouldBe(Schedule);
        new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m).GetHashCode().ShouldBe(Schedule.GetHashCode());
        new SellerFeeSchedule([0.05m], minimum: 0.01m).ShouldNotBe(Schedule);
    }

    [Test]
    public void RejectsInvalidSchedules()
    {
        Should.Throw<TradingInvariantException>(() => new SellerFeeSchedule([], minimum: 0.01m));
        Should.Throw<TradingInvariantException>(() => new SellerFeeSchedule([1m], minimum: 0.01m));
        Should.Throw<TradingInvariantException>(() => new SellerFeeSchedule([0.05m], minimum: -0.01m));
        Should.Throw<TradingInvariantException>(() => Schedule.BuyerPrice(sellerAmount: 0.015m, Precision));
    }

    [Test]
    public void BuysWithoutAFeeAndSellsForTheSellerAmount()
    {
        var account = new TradingAccount(new ExecutionSettings(startingCapital: 1m, slippage: 0m, Schedule), WholeItems);

        Order buy = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 0.40m, LimitPrice: 0.20m, Tag: null, "node", EligibleFromCandleIndex: 0))
            .Order!;
        Fill bought = account.Execute(buy.Id, referencePrice: 0m).Fill!;

        bought.Fee.ShouldBe(0m);
        bought.QuoteChange.ShouldBe(-0.40m);
        bought.BaseChange.ShouldBe(2m);

        Order sell =
            account.Submit(new OrderRequest(OrderSide.Sell, OrderType.Limit, Amount: 2m, LimitPrice: 0.22m, Tag: null, "node", EligibleFromCandleIndex: 0)).Order!;

        sell.LimitPrice.ShouldBe(0.23m);

        Fill sold = account.Execute(sell.Id, referencePrice: 0m).Fill!;

        sold.Fee.ShouldBe(0.06m);
        sold.QuoteChange.ShouldBe(0.40m);
        sold.RealizedPnl.ShouldBe(0m);
        account.Portfolio.QuoteTotal.ShouldBe(1m);
        account.Portfolio.FeesQuote.ShouldBe(0.06m);
    }

    [Test]
    public void KeepsRatesZeroWithASchedule()
    {
        var settings = new ExecutionSettings(startingCapital: 1m, slippage: 0m, Schedule);

        settings.LimitFeeRate.ShouldBe(0m);
        settings.MarketFeeRate.ShouldBe(0m);
        settings.SellerFees.ShouldBe(Schedule);
        new ExecutionSettings(startingCapital: 1m, slippage: 0m, Schedule).ShouldBe(settings);
        new ExecutionSettings(startingCapital: 1m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m).ShouldNotBe(settings);
    }

    private static decimal Number(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}