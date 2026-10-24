using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

public sealed class TradingAccountPropertyTests
{
    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.0001m,
        priceTick: 0.01m,
        minQuantity: 0.0001m,
        minNotional: 5m,
        basePrecision: 8,
        quotePrecision: 6);

    private static IEnumerable<int> Seeds => Enumerable.Range(start: 1, count: 200);

    private static IReadOnlyList<string> Run(int seed)
    {
        var random = new Random(seed);
        var account = new TradingAccount(new ExecutionSettings(startingCapital: 10_000m, slippage: 0.0005m, limitFeeRate: 0.001m, marketFeeRate: 0.0015m), Rules);
        var log = new List<string>();
        var price = 100m;

        for (var step = 0; step < 400; step++)
        {
            price = Math.Max(val1: 1m, decimal.Round(price * (1m + random.Next(minValue: -300, maxValue: 301) / 10_000m), decimals: 2));
            int action = random.Next(5);
            decimal fraction = random.Next(minValue: 1, maxValue: 101) / 100m;
            OrderType type = random.Next(2) == 0 ? OrderType.Market : OrderType.Limit;
            decimal? limit = type == OrderType.Limit ? price * (1m + random.Next(minValue: -500, maxValue: 501) / 10_000m) : null;

            if (action == 0)
            {
                log.Add(Describe(account.Submit(new OrderRequest(OrderSide.Buy, type, account.Portfolio.QuoteAvailable * fraction, limit, Tag: null, "buy", step))));
            }
            else if (action == 1)
            {
                log.Add(Describe(account.Submit(new OrderRequest(OrderSide.Sell, type, account.Portfolio.BaseAvailable * fraction, limit, Tag: null, "sell", step))));
            }
            else if (action == 2 && account.Orders.Active.Count > 0)
            {
                Order order = account.Orders.Active[random.Next(account.Orders.Active.Count)];
                OrderExecution execution = account.Execute(order.Id, price);
                log.Add($"exec {order.Id} {execution.Fill?.Price} {execution.Fill?.GrossBase} {execution.Fill?.Fee} {execution.Rejection?.Reason}");
            }
            else if (action == 3 && account.Orders.Active.Count > 0)
            {
                log.Add($"cancel {account.Cancel(account.Orders.Active[random.Next(account.Orders.Active.Count)].Id)?.Id}");
            }

            AssertInvariants(account, price);
        }

        account.ReleaseAll();
        AssertInvariants(account, price);
        log.Add($"final {account.Portfolio.QuoteTotal} {account.Portfolio.BaseTotal} {account.Portfolio.CostBasis} {account.Portfolio.RealizedPnl}");

        return log;
    }

    private static string Describe(OrderSubmission submission)
    {
        return submission.IsAccepted
            ? $"submit {submission.Order!.Id} {submission.Order.Quantity} {submission.Order.Reservation}"
            : $"reject {submission.Rejection!.Reason}";
    }

    /// <summary>
    ///     Стоимость позиции и реализованный результат делятся пропорционально и могут терять разряды за пределами 28 значащих
    ///     цифр decimal, поэтому сверка equity допускает 1e-10; балансы и резервы обязаны сходиться точно.
    /// </summary>
    private static void AssertInvariants(TradingAccount account, decimal close)
    {
        Portfolio portfolio = account.Portfolio;
        portfolio.QuoteTotal.ShouldBeGreaterThanOrEqualTo(0m);
        portfolio.BaseTotal.ShouldBeGreaterThanOrEqualTo(0m);
        portfolio.QuoteAvailable.ShouldBeGreaterThanOrEqualTo(0m);
        portfolio.BaseAvailable.ShouldBeGreaterThanOrEqualTo(0m);
        portfolio.QuoteReserved.ShouldBe(account.Orders.Active.Where(order => order.Side == OrderSide.Buy).Sum(order => order.Reservation));
        portfolio.BaseReserved.ShouldBe(account.Orders.Active.Where(order => order.Side == OrderSide.Sell).Sum(order => order.Reservation));
        (portfolio.Equity(close) - portfolio.StartingCapital).ShouldBe(portfolio.RealizedPnl + portfolio.UnrealizedPnl(close), tolerance: 0.0000000001m);
    }

    [TestCaseSource(nameof(Seeds))]
    public void RandomActivity_KeepsBalancesConsistent_AndIsDeterministic(int seed)
    {
        Run(seed).ShouldBe(Run(seed));
    }
}