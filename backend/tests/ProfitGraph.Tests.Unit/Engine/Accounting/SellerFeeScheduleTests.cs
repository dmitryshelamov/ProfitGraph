using System.Globalization;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

/// <summary>
///     Invented rates and minimum from Profit Fox Designer ADR 0082: 5% and 10% with one minor unit (0.01) each, with an
///     increment of one minor unit and of a hundred, as a KZT wallet counts whole tenge.
/// </summary>
public sealed class SellerFeeScheduleTests
{
    private const int Precision = 2;

    private static readonly SellerFeeSchedule Schedule = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);
    private static readonly SellerFeeSchedule WholeTenge = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m, increment: 100);

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
    public void AnIncrementOfOneMinorUnitKeepsTheFlooredFeeWithItsMinimum()
    {
        foreach (decimal minimum in new[] { 0m, 0.01m, 0.05m })
        {
            var schedule = new SellerFeeSchedule([0.05m, 0.10m], minimum);

            for (var seller = 0; seller <= 5000; seller++)
            {
                decimal amount = seller / 100m;
                decimal expected = amount + Math.Max(minimum, decimal.Floor(seller * 0.05m) / 100m) + Math.Max(minimum, decimal.Floor(seller * 0.10m) / 100m);

                schedule.BuyerPrice(amount, Precision).ShouldBe(seller == 0 ? 0m : expected);
            }
        }
    }

    [TestCase("1.00", "3.00")]
    [TestCase("20.00", "23.00")]
    [TestCase("29.00", "33.00")]
    [TestCase("30.00", "35.00")]
    [TestCase("100.00", "115.00")]
    public void RoundsEveryComponentOfAWholeTengeWalletToWholeTenge(string seller, string buyer)
    {
        WholeTenge.BuyerPrice(Number(seller), Precision).ShouldBe(Number(buyer));
    }

    [TestCase("33.00", "29.00")]
    [TestCase("34.00", "29.00")]
    [TestCase("33.50", "29.00")]
    [TestCase("2.99", "0")]
    public void GivesTheSellerOfAWholeTengeWalletOnlyWholeTenge(string buyer, string seller)
    {
        WholeTenge.SellerAmount(Number(buyer), Precision).ShouldBe(Number(seller));
    }

    [TestCase("33.00", "33.00")]
    [TestCase("34.00", "35.00")]
    [TestCase("2.99", "3.00")]
    [TestCase("0.01", "3.00")]
    public void RaisesAPriceOfAWholeTengeWalletToTheNextReachableOne(string requested, string raised)
    {
        WholeTenge.RaiseToRepresentable(Number(requested), Precision).ShouldBe(Number(raised));
    }

    [Test]
    public void EveryReachablePriceRoundTrips()
    {
        var fives = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m, increment: 5);

        foreach (SellerFeeSchedule schedule in new[] { Schedule, fives, WholeTenge })
        {
            for (var step = 1; step <= 1000; step++)
            {
                decimal seller = step * schedule.Increment / 100m;
                decimal buyer = schedule.BuyerPrice(seller, Precision);

                schedule.SellerAmount(buyer, Precision).ShouldBe(seller);
                schedule.RaiseToRepresentable(buyer, Precision).ShouldBe(buyer);
            }
        }
    }

    [Test]
    public void RoundsToTheNearestIncrementAfterTheFloorAndTheMinimum()
    {
        var fives = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m, increment: 5);

        fives.BuyerPrice(sellerAmount: 1.50m, Precision).ShouldBe(1.70m);
        fives.Fee(quantity: 1m, buyerPrice: 1.70m, Precision).ShouldBe(0.20m);
        fives.SellerAmount(buyerPrice: 1.70m, Precision).ShouldBe(1.50m);
    }

    [Test]
    public void ChargesAWholeTengeWalletPerUnit()
    {
        WholeTenge.Fee(quantity: 3m, buyerPrice: 35m, Precision).ShouldBe(15m);
        WholeTenge.Fee(quantity: 10m, buyerPrice: 3m, Precision).ShouldBe(20m);
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
        new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m, increment: 100).ShouldBe(WholeTenge);
        WholeTenge.ShouldNotBe(Schedule);
        Schedule.Increment.ShouldBe(1);
    }

    [Test]
    public void RejectsInvalidSchedules()
    {
        Should.Throw<TradingInvariantException>(() => new SellerFeeSchedule([], minimum: 0.01m));
        Should.Throw<TradingInvariantException>(() => new SellerFeeSchedule([1m], minimum: 0.01m));
        Should.Throw<TradingInvariantException>(() => new SellerFeeSchedule([0.05m], minimum: -0.01m));
        Should.Throw<TradingInvariantException>(() => new SellerFeeSchedule([0.05m], minimum: 0.01m, increment: 0));
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
    public void SellsAWholeTengeItemAtTheNextWholeTengePrice()
    {
        var rules = new InstrumentRules(quantityStep: 1m, priceTick: 1m, minQuantity: 1m, minNotional: 3m, basePrecision: 0, Precision);
        var account = new TradingAccount(new ExecutionSettings(startingCapital: 100m, slippage: 0m, WholeTenge), rules);

        Order buy = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 60m, LimitPrice: 30m, Tag: null, "node", EligibleFromCandleIndex: 0))
            .Order!;
        account.Execute(buy.Id, referencePrice: 0m);
        Order sell =
            account.Submit(new OrderRequest(OrderSide.Sell, OrderType.Limit, Amount: 2m, LimitPrice: 34m, Tag: null, "node", EligibleFromCandleIndex: 0)).Order!;

        sell.LimitPrice.ShouldBe(35m);

        Fill sold = account.Execute(sell.Id, referencePrice: 0m).Fill!;

        sold.Fee.ShouldBe(10m);
        sold.QuoteChange.ShouldBe(60m);
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