using System.Globalization;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

public sealed class OrderSizingServiceTests
{
    [TestCase(OrderSide.Buy, OrderType.Market, "100")]
    [TestCase(OrderSide.Buy, OrderType.Limit, "100")]
    [TestCase(OrderSide.Sell, OrderType.Market, "2")]
    [TestCase(OrderSide.Sell, OrderType.Limit, "2")]
    [TestCase(OrderSide.Buy, OrderType.Market, "0")]
    [TestCase(OrderSide.Buy, OrderType.Market, "0.5")]
    [TestCase(OrderSide.Buy, OrderType.Market, "1001")]
    public void PreviewBehaviorKeepsExactSharedSizingResults(OrderSide side, OrderType type, string amountText)
    {
        decimal amount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        NodeTradingEvaluationContext context = CreatePreviewContext(side, type, amount);
        var request = new OrderRequest(side, type, amount, type == OrderType.Limit ? 100m : null, Tag: null, "preview", EligibleFromCandleIndex: 0);
        OrderSizingResult expected = context.Sizing.Resolve(request, context.Dispatch.Rules, context.Dispatch.Settings, context.Dispatch.Account, referencePrice: 100m);
        context.Node.Definition.EvaluateTrading(context);
        context.Values["valid"].Boolean.ShouldBe(expected.IsValid);
        context.Values[side == OrderSide.Buy ? "requiredQuote" : "requiredBase"].Number.ShouldBe(expected.Reservation);

        if (expected.Quantity.HasValue)
        {
            context.Values["normalizedQuantity"].Number.ShouldBe(expected.Quantity.Value);
        }

        if (expected.Reason.HasValue)
        {
            context.Values["reason"].Reason.ShouldBe(expected.Reason.Value);
        }
    }

    [Test]
    public void PreviewEvaluationDoesNotAllocatePerDispatch()
    {
        NodeTradingEvaluationContext context = CreatePreviewContext(OrderSide.Buy, OrderType.Market, amount: 100m);

        for (var index = 0; index < 100; index++)
        {
            context.Node.Definition.EvaluateTrading(context);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1000; index++)
        {
            context.Node.Definition.EvaluateTrading(context);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.ShouldBeLessThan(1024);
        context.Values["normalizedQuantity"].Number.ShouldBe(1m);
    }

    private static NodeTradingEvaluationContext CreatePreviewContext(OrderSide side, OrderType type, decimal amount)
    {
        var definition = new OrderSizePreviewNodeDefinition();
        var nodeParameters = new Dictionary<string, ParamValue>
        {
            ["side"] = EngineGraphFixture.Text(side.ToString()),
            ["type"] = EngineGraphFixture.Text(type.ToString()),
            [side == OrderSide.Buy ? "amount" : "quantity"] = EngineGraphFixture.Number(100m)
        };

        if (type == OrderType.Limit)
        {
            nodeParameters["price"] = EngineGraphFixture.Number(100m);
        }

        var node = new GraphNode("preview", definition.TypeKey, schemaVersion: 1, nodeParameters);
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(new StrategyGraph([node], []), [definition]);
        result.Validation.Diagnostics.ShouldBeEmpty();
        CompiledTradingNode compiled = result.Graph.ShouldNotBeNull().Nodes.Single();
        var rules = new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.01m, minQuantity: 0.000001m, minNotional: 1m, basePrecision: 8, quotePrecision: 2);
        var settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var account = new TradingAccount(settings, rules);
        var market = new ReachedMarketContext(
            EngineGraphFixture.Candle(index: 0, close: 100m),
            candleIndex: 0,
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 100m,
            eligibleFromCandleIndex: 0);
        var parameters = new ResolvedEngineParameters(
            new Dictionary<string, EngineValue>
            {
                ["amount"] = EngineValue.Numeric(ValueType.QuoteAmount, amount),
                ["quantity"] = EngineValue.Numeric(ValueType.BaseAmount, amount),
                ["price"] = EngineValue.Numeric(ValueType.Price, value: 100m)
            },
            new Dictionary<string, string> { ["side"] = side.ToString(), ["type"] = type.ToString() });

        return new NodeTradingEvaluationContext(
            compiled,
            parameters,
            new DispatchEvaluationContext(AccountSnapshot.Capture(account), rules, settings, market),
            new Dictionary<string, EngineValue>(),
            new OrderSizingService());
    }
}