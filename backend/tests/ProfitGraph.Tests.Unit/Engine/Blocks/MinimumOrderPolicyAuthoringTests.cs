using System.Globalization;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Blocks;

public sealed class MinimumOrderPolicyAuthoringTests
{
    [TestCase("Buy", "RaiseToExchangeMinimum", arg3: true)]
    [TestCase("Buy", "Reject", arg3: true)]
    [TestCase("Buy", arg2: null, arg3: true)]
    [TestCase("Buy", "Unknown", arg3: false)]
    [TestCase("Sell", "RaiseToExchangeMinimum", arg3: false)]
    public void AuthoringValidatesBuyPolicyAndLowersOnlyTheOptIn(string side, string? policy, bool valid)
    {
        var block = new StrategyBlock(
            "order",
            "order",
            orderKind: "Limit",
            side: side,
            tag: new ValueExpression("constant", ValueType.Tag, text: "buy"),
            amount: Number(value: 2m, ValueType.QuoteAmount),
            quantity: Number(value: 1m, ValueType.BaseAmount),
            price: Number(value: 10m, ValueType.Price),
            minimumOrderPolicy: policy);
        var definition = new StrategyDefinition(
            [],
            [],
            [new StrategyBlock("start", "event", source: "OnStart"), block],
            [new StrategyWire(new StrategyExit("start", "out"), "order")]);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, ProductionTradingNodeTests.Catalog);
        result.Diagnostics.Any().ShouldBe(!valid);

        if (valid)
        {
            GraphNode node = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition).Graph.Nodes.Single(node => node.Id == "order");
            node.Parameters.ContainsKey("minimumOrderPolicy").ShouldBe(policy == "RaiseToExchangeMinimum");
        }
    }

    [TestCase("4.99", arg2: false)]
    [TestCase("5", arg2: true)]
    [TestCase("absent", arg2: true)]
    [TestCase("unavailable", arg2: false)]
    public void PreviewUsesRaisedCostAgainstTheDeclaredRemainingAllocation(string limit, bool valid)
    {
        decimal quoteLimit = limit is "absent" or "unavailable" ? 5m : decimal.Parse(limit, CultureInfo.InvariantCulture);
        var operands = new Dictionary<string, ValueExpression>
        {
            ["amount"] = Number(value: 2m, ValueType.QuoteAmount),
            ["price"] = Number(value: 10m, ValueType.Price)
        };

        if (limit != "absent")
        {
            operands["quoteLimit"] = Number(quoteLimit, ValueType.QuoteAmount);
        }

        var check = new StrategyCheck("OrderSizePreviewValid", operands: operands, side: "Buy", orderKind: "Limit", minimumOrderPolicy: "RaiseToExchangeMinimum");
        var definition = new StrategyDefinition(
            [],
            [],
            [new StrategyBlock("start", "event", source: "OnStart"), new StrategyBlock("guard", "condition", join: "all", checks: [check])],
            [new StrategyWire(new StrategyExit("start", "out"), "guard")]);
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition);
        CompiledTradingGraph plan = new TradingGraphCompiler().Compile(lowered.Graph, ProductionTradingNodeTests.Catalog).Graph.ShouldNotBeNull();
        CompiledTradingNode node = plan.Nodes.Single(node => node.TradingBehavior == TradingNodeBehavior.OrderSizePreview);
        var rules = new InstrumentRules(quantityStep: 0.1m, priceTick: 0.1m, minQuantity: 0.1m, minNotional: 5m, basePrecision: 2, quotePrecision: 2);
        var settings = new ExecutionSettings(startingCapital: 100m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var account = new TradingAccount(settings, rules);
        var parameters = new ResolvedEngineParameters(
            new Dictionary<string, EngineValue>
            {
                ["amount"] = EngineValue.Numeric(ValueType.QuoteAmount, value: 2m),
                ["price"] = EngineValue.Numeric(ValueType.Price, value: 10m),
                ["quoteLimit"] = limit is "absent" or "unavailable"
                    ? EngineValue.Unavailable(ValueType.QuoteAmount)
                    : EngineValue.Numeric(ValueType.QuoteAmount, quoteLimit)
            },
            new Dictionary<string, string> { ["side"] = "Buy", ["type"] = "Limit", ["minimumOrderPolicy"] = "RaiseToExchangeMinimum" });
        var market = new ReachedMarketContext(
            EngineGraphFixture.Candle(index: 0, close: 10m),
            candleIndex: 0,
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 10m,
            eligibleFromCandleIndex: 0);
        var context = new NodeTradingEvaluationContext(
            node,
            parameters,
            new DispatchEvaluationContext(account.Capture(), rules, settings, market),
            new Dictionary<string, EngineValue>(),
            new OrderSizingService());
        node.Definition.EvaluateTrading(context);

        if (limit == "unavailable")
        {
            context.Values.ShouldBeEmpty();

            return;
        }

        context.Values["valid"].Boolean.ShouldBe(valid);

        if (!valid)
        {
            context.Values["reason"].Reason.ShouldBe(RejectionReason.InsufficientQuote);
        }
    }

    private static ValueExpression Number(decimal value, ValueType type)
    {
        return new ValueExpression("constant", type, value);
    }
}