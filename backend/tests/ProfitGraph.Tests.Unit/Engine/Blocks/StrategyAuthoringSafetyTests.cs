using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Blocks;

public sealed class StrategyAuthoringSafetyTests
{
    [TestCase(0)]
    [TestCase(-1)]
    public void KnownInvalidConversionPriceIsReportedOnItsOrder(int price)
    {
        StrategyBlock trade = Trade(Number(price, ValueType.Price));
        StrategyDefinition seed = AfterEntry(trade);
        var parameter = new StrategyParameter("price", ValueType.Price, price);
        var definition = new StrategyDefinition(
            [parameter],
            [new StrategyValue("reference", new ValueExpression("parameter", name: "price"))],
            seed.Blocks.Select(block => block.Id == "trade" ? Trade(new ValueExpression("value", name: "reference")) : block),
            seed.Wires);
        new StrategyDefinitionValidator().Validate(definition, Catalog)
            .Diagnostics.ShouldContain(error => error.BlockId == "trade" && error.Message.Contains("positive"));
    }

    [TestCase(100)]
    [TestCase(101)]
    public void DynamicNonpositivePriceSuppressesInverseSellWithoutArithmeticFailure(int subtraction)
    {
        var price = new ValueExpression(
            "arithmetic",
            operation: "Subtract",
            arguments: new Dictionary<string, ValueExpression>
            {
                ["a"] = new ValueExpression("candle", field: "Close"), ["b"] = Number(subtraction, ValueType.Price)
            });
        StrategyDefinition seeded = AfterEntry(Trade(price));
        var definition = new StrategyDefinition(
            [],
            [],
            seeded.Blocks.Append(new StrategyBlock("clock", "event", source: "OnCandleClosed")),
            [seeded.Wires[0], new StrategyWire(new StrategyExit("clock", "out"), "trade")]);
        BacktestResult result = Replay(definition, [100m, 100m]);
        result.Diagnostics.ShouldBeEmpty();
        result.Ledger.ShouldNotContain(entry => entry.Order != null && entry.Order.OriginNodeId == "trade");
    }

    [Test]
    public void UnavailableConversionPriceSuppressesInverseOrder()
    {
        StrategyBlock trade = Trade(new ValueExpression("position", field: "averageCost"));
        var definition = new StrategyDefinition(
            [],
            [],
            [new StrategyBlock("clock", "event", source: "OnCandleClosed"), trade],
            [new StrategyWire(new StrategyExit("clock", "out"), "trade")]);
        BacktestResult result = Replay(definition, [100m, 100m]);
        result.Diagnostics.ShouldBeEmpty();
        result.Ledger.ShouldNotContain(entry => entry.Order != null);
    }

    [Test]
    public void SmallestPositiveDecimalConversionPriceIsPreservedExactly()
    {
        const decimal epsilon = 0.0000000000000000000000000001m;
        var trade = new StrategyBlock(
            "trade",
            "order",
            orderKind: "Market",
            side: "Sell",
            tag: new ValueExpression("constant", ValueType.Tag, text: "trade"),
            amount: Number(epsilon, ValueType.QuoteAmount),
            price: Number(epsilon, ValueType.Price),
            sizeMode: "amount");
        BacktestResult result = Replay(AfterEntry(trade), [100m]);
        result.Diagnostics.ShouldBeEmpty();
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.OriginNodeId == "trade").Order!.Quantity.ShouldBe(1m);
    }

    [Test]
    public void InverseLimitLowersReferencePriceOnlyOnce()
    {
        var trade = new StrategyBlock(
            "trade",
            "order",
            orderKind: "Limit",
            side: "Sell",
            amount: Number(value: 25m, ValueType.QuoteAmount),
            price: new ValueExpression("candle", field: "Close"),
            sizeMode: "amount");
        StrategyLoweringResult result = new StrategyDefinitionLowerer(Catalog).Lower(AfterEntry(trade));
        result.Graph.Nodes.Count(node => node.TypeKey == "CandleField").ShouldBe(1);
    }

    [TestCase("Buy", "quantity")]
    [TestCase("Sell", "amount")]
    public void PreviewSupportsIndependentSizeAndReportsMissingPriceOnCheck(string side, string sizeMode)
    {
        var check = new StrategyCheck(
            "OrderSizePreviewValid",
            operands: new Dictionary<string, ValueExpression>
            {
                ["amount"] = Number(value: 25m, ValueType.QuoteAmount), ["quantity"] = Number(value: 0.25m, ValueType.BaseAmount),
                ["price"] = Number(value: 100m, ValueType.Price)
            },
            side: side,
            orderKind: "Market",
            sizeMode: sizeMode);
        StrategyDefinition definition = Conditions(check);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        var missing = new StrategyCheck(
            "OrderSizePreviewValid",
            operands: check.Operands.Where(pair => pair.Key != "price").ToDictionary(),
            side: side,
            orderKind: "Market",
            sizeMode: sizeMode);
        new StrategyDefinitionValidator().Validate(Conditions(missing), Catalog).Diagnostics.ShouldContain(error => error.BlockId == "check" && error.CheckIndex == 1);
    }

    [Test]
    public void GraphTypeDiagnosticsIdentifyTheExactCheck()
    {
        var check = new StrategyCheck(
            "Compare",
            ">",
            new Dictionary<string, ValueExpression>
            {
                ["a"] = Number(value: 1m, ValueType.QuoteAmount), ["b"] = Number(value: 1m, ValueType.BaseAmount)
            });
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(Conditions(check), Catalog);
        result.Diagnostics.ShouldNotBeEmpty();
        result.Diagnostics.ShouldAllBe(error => error.BlockId == "check" && error.CheckIndex == 1);
    }

    [Test]
    public void DictionaryInsertionOrderDoesNotAffectLoweredGraph()
    {
        ValueExpression Expression(bool reversed) => new ValueExpression(
            "arithmetic",
            operation: "Add",
            arguments: (reversed
                ? new[]
                {
                    new KeyValuePair<string, ValueExpression>("b", Number(value: 2m, ValueType.Price)),
                    new KeyValuePair<string, ValueExpression>("a", Number(value: 1m, ValueType.Price))
                }
                : new[]
                {
                    new KeyValuePair<string, ValueExpression>("a", Number(value: 1m, ValueType.Price)),
                    new KeyValuePair<string, ValueExpression>("b", Number(value: 2m, ValueType.Price))
                }).ToDictionary());

        StrategyLoweringResult first = new StrategyDefinitionLowerer(Catalog).Lower(AfterEntry(Trade(Expression(false))));
        StrategyLoweringResult second = new StrategyDefinitionLowerer(Catalog).Lower(AfterEntry(Trade(Expression(true))));
        second.Graph.Nodes.Select(node => node.Id).ShouldBe(first.Graph.Nodes.Select(node => node.Id));
        second.Graph.Connections.ShouldBe(first.Graph.Connections);
    }

    [Test]
    public void ReservedCompositeBlockIdIsRejected()
    {
        var definition = new StrategyDefinition([], [], [new StrategyBlock("composite:collision", "event", source: "OnStart")], []);
        new StrategyDefinitionValidator().Validate(definition, Catalog).IsValid.ShouldBeFalse();
    }

    [Test]
    public void ExcessiveInlineDepthStopsBeforeLowering()
    {
        ValueExpression expression = Number(value: 1m, ValueType.Price);

        for (var index = 0; index <= StrategyAuthoringLimits.InlineDepth; index++)
        {
            expression = new ValueExpression("previous", arguments: new Dictionary<string, ValueExpression> { ["in"] = expression });
        }

        StrategyDefinition definition = AfterEntry(Trade(expression));
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldContain(error => error.BlockId == "trade" && error.Message.Contains("limit"));
        Should.Throw<ArgumentException>(() => new StrategyDefinitionLowerer(Catalog).Lower(definition));
    }

    [Test]
    public void InvalidNullValueNameProducesDiagnosticWithoutThrowing()
    {
        var definition = new StrategyDefinition(
            [],
            [new StrategyValue(null!, Number(value: 1m, ValueType.Price))],
            [new StrategyBlock("start", "event", source: "OnStart")],
            []);
        new StrategyDefinitionValidator().Validate(definition, Catalog).IsValid.ShouldBeFalse();
    }

    [Test]
    public void ExcessiveExpandedNamedDepthStopsBeforeRecursiveValidation()
    {
        var values = new List<StrategyValue> { new StrategyValue("v0", Number(value: 1m, ValueType.Price)) };

        for (var index = 1; index < StrategyAuthoringLimits.Values; index++)
        {
            var reference = new ValueExpression("value", name: "v" + (index - 1));
            values.Add(new StrategyValue("v" + index, new ValueExpression("previous", arguments: new Dictionary<string, ValueExpression> { ["in"] = reference })));
        }

        var definition = new StrategyDefinition([], values, [new StrategyBlock("start", "event", source: "OnStart")], []);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldContain(error => error.ValueName != null && error.Message.Contains("limit"));
        Should.Throw<ArgumentException>(() => new StrategyDefinitionLowerer(Catalog).Lower(definition));
    }

    [Test]
    public void ExcessiveBlocksAndTextStopBeforeLowering()
    {
        var definition = new StrategyDefinition(
            [],
            [],
            Enumerable.Range(start: 0, StrategyAuthoringLimits.Blocks + 1)
                .Select(index => new StrategyBlock("event" + index, "event", source: "OnStart")),
            []);
        new StrategyDefinitionValidator().Validate(definition, Catalog).IsValid.ShouldBeFalse();
        var text = new StrategyDefinition([], [], [new StrategyBlock("event", "event", new string(c: 'x', StrategyAuthoringLimits.TextLength + 1), "OnStart")], []);
        new StrategyDefinitionValidator().Validate(text, Catalog).IsValid.ShouldBeFalse();
    }

    [Test]
    public void DiagnosticOutputIsBoundedWithExplicitTruncationIndicator()
    {
        StrategyCheck[] checks = Enumerable.Range(start: 0, StrategyAuthoringLimits.ChecksPerBlock).Select(_ => new StrategyCheck("Unknown")).ToArray();
        var definition = new StrategyDefinition(
            [],
            [],
            Enumerable.Range(start: 0, count: 8).Select(index => new StrategyBlock("condition" + index, "condition", join: "all", checks: checks)),
            []);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.Diagnostics.Count.ShouldBe(StrategyAuthoringLimits.Diagnostics + 1);
        result.Diagnostics.Last().Message.ShouldContain("truncated");
    }

    private static StrategyBlock Trade(ValueExpression price)
    {
        return new StrategyBlock(
            "trade",
            "order",
            orderKind: "Market",
            side: "Sell",
            tag: new ValueExpression("constant", ValueType.Tag, text: "trade"),
            amount: Number(value: 25m, ValueType.QuoteAmount),
            price: price,
            sizeMode: "amount");
    }

    private static StrategyDefinition Conditions(StrategyCheck check)
    {
        return new StrategyDefinition(
            [],
            [],
            [
                new StrategyBlock("start", "event", source: "OnStart"),
                new StrategyBlock("check", "condition", join: "all", checks: [new StrategyCheck("PositionFlat"), check])
            ],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
    }

    private static StrategyDefinition AfterEntry(StrategyBlock trade)
    {
        return new StrategyDefinition(
            [],
            [],
            [
                new StrategyBlock("start", "event", source: "OnStart"),
                new StrategyBlock(
                    "seed",
                    "order",
                    orderKind: "Market",
                    side: "Buy",
                    tag: new ValueExpression("constant", ValueType.Tag, text: "seed"),
                    amount: Number(value: 100m, ValueType.QuoteAmount)),
                trade
            ],
            [new StrategyWire(new StrategyExit("start", "out"), "seed"), new StrategyWire(new StrategyExit("seed", "filled"), "trade")]);
    }

    private static ValueExpression Number(decimal value, ValueType type)
    {
        return new ValueExpression("constant", type, value);
    }

    private static BacktestResult Replay(StrategyDefinition definition, decimal[] prices, decimal priceTick = 0.001m)
    {
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        TradingGraphCompilationResult compiled = new TradingGraphCompiler().Compile(lowered.Graph, Catalog);
        compiled.Validation.Diagnostics.ShouldBeEmpty();
        var input = new BacktestInput(
            compiled.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            prices.Select((price, index) => EngineGraphFixture.Candle(index, price)));

        return new BacktestEngine().Run(input);
    }
}