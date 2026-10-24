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

public sealed class OrderBlockSizeTests
{
    [TestCase("Buy", "amount", "Market")]
    [TestCase("Buy", "quantity", "Market")]
    [TestCase("Sell", "amount", "Market")]
    [TestCase("Sell", "quantity", "Market")]
    [TestCase("Buy", "amount", "Limit")]
    [TestCase("Buy", "quantity", "Limit")]
    [TestCase("Sell", "amount", "Limit")]
    [TestCase("Sell", "quantity", "Limit")]
    public void BothSidesSupportBothSizeModesWithoutExecutingInactiveSettings(string side, string sizeMode, string orderKind)
    {
        var trade = new StrategyBlock(
            "trade",
            "order",
            orderKind: orderKind,
            side: side,
            tag: new ValueExpression("constant", ValueType.Tag, text: "trade"),
            amount: Number(value: 25m, ValueType.QuoteAmount),
            quantity: Number(value: 0.25m, ValueType.BaseAmount),
            price: Number(value: 100m, ValueType.Price),
            sizeMode: sizeMode);
        StrategyDefinition definition = AfterEntry(trade);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        lowered.Graph.Connections.Where(connection => connection.Target.NodeId == "trade")
            .Select(connection => connection.Target.PortKey)
            .ShouldBe(
                orderKind == "Limit" ? new[] { side == "Buy" ? "amount" : "quantity", "price", "trigger" } : [side == "Buy" ? "amount" : "quantity", "trigger"],
                ignoreOrder: true);
        BacktestResult result = Replay(definition, [100m]);
        Fill fill = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Order!.OriginNodeId == "trade").Fill.ShouldNotBeNull();
        fill.GrossBase.ShouldBe(0.25m);
        fill.QuoteChange.ShouldBe(side == "Buy" ? -25m : 25m);
        result.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void MarketQuantityBuyFreezesConvertedBudgetAtSignalBeforeNextOpenGap()
    {
        var trade = new StrategyBlock(
            "trade",
            "order",
            orderKind: "Market",
            side: "Buy",
            tag: new ValueExpression("constant", ValueType.Tag, text: "trade"),
            quantity: Number(value: 1m, ValueType.BaseAmount),
            price: new ValueExpression("candle", field: "Close"),
            sizeMode: "quantity");
        var definition = new StrategyDefinition(
            [],
            [],
            [new StrategyBlock("clock", "event", source: "OnCandleClosed"), trade],
            [new StrategyWire(new StrategyExit("clock", "out"), "trade")]);
        BacktestResult result = Replay(definition, [100m, 200m]);
        result.Ledger.First(entry => entry.Kind == BacktestLedgerKind.Submitted).Order!.QuoteBudget.ShouldBe(100m);
        Fill fill = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled).Fill.ShouldNotBeNull();
        fill.GrossBase.ShouldBe(0.5m);
        fill.QuoteChange.ShouldBe(-100m);
    }

    [Test]
    public void MarketAmountSellFreezesConvertedQuantityAtSignalBeforeNextOpenGap()
    {
        var trade = new StrategyBlock(
            "trade",
            "order",
            orderKind: "Market",
            side: "Sell",
            tag: new ValueExpression("constant", ValueType.Tag, text: "trade"),
            amount: Number(value: 50m, ValueType.QuoteAmount),
            price: new ValueExpression("candle", field: "Close"),
            sizeMode: "amount");
        StrategyDefinition seed = AfterEntry(trade);
        var definition = new StrategyDefinition(
            [],
            [],
            seed.Blocks.Append(new StrategyBlock("clock", "event", source: "OnCandleClosed")),
            [seed.Wires[0], new StrategyWire(new StrategyExit("clock", "out"), "trade")]);
        BacktestResult result = Replay(definition, [100m, 200m]);
        result.Ledger.First(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.OriginNodeId == "trade").Order!.Quantity.ShouldBe(0.5m);
        Fill fill = result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Order!.OriginNodeId == "trade").Fill.ShouldNotBeNull();
        fill.GrossBase.ShouldBe(0.5m);
        fill.QuoteChange.ShouldBe(100m);
    }

    [TestCase("Buy", "quantity")]
    [TestCase("Sell", "amount")]
    public void InverseMarketSizeRequiresExplicitConversionPrice(string side, string sizeMode)
    {
        var trade = new StrategyBlock(
            "trade",
            "order",
            orderKind: "Market",
            side: side,
            amount: Number(value: 25m, ValueType.QuoteAmount),
            quantity: Number(value: 0.25m, ValueType.BaseAmount),
            sizeMode: sizeMode);
        new StrategyDefinitionValidator().Validate(AfterEntry(trade), Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.BlockId == "trade");
    }

    [Test]
    public void WrongSizeModeAndSelectedUnitsIdentifyTheVisibleOrder()
    {
        var invalidMode = new StrategyBlock("trade", "order", orderKind: "Market", side: "Buy", sizeMode: "unknown");
        new StrategyDefinitionValidator().Validate(AfterEntry(invalidMode), Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.BlockId == "trade");
        var invalidUnits = new StrategyBlock(
            "trade",
            "order",
            orderKind: "Market",
            side: "Buy",
            quantity: Number(value: 1m, ValueType.QuoteAmount),
            price: Number(value: 100m, ValueType.Price),
            sizeMode: "quantity");
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(AfterEntry(invalidUnits), Catalog);
        result.IsValid.ShouldBeFalse();
        result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.BlockId == "trade" && !diagnostic.Message.Contains('$'));
    }

    [Test]
    public void LimitQuantityConversionRetainsExistingPriceNormalizationRules()
    {
        var trade = new StrategyBlock(
            "trade",
            "order",
            orderKind: "Limit",
            side: "Buy",
            tag: new ValueExpression("constant", ValueType.Tag, text: "trade"),
            quantity: Number(value: 1m, ValueType.BaseAmount),
            price: Number(value: 100.09m, ValueType.Price),
            sizeMode: "quantity");
        BacktestResult result = Replay(AfterEntry(trade), [100m], priceTick: 0.1m);
        BacktestLedgerEntry entry = result.Ledger.Single(item => item.Kind == BacktestLedgerKind.Submitted && item.Order!.OriginNodeId == "trade");
        entry.Order!.LimitPrice.ShouldBe(100m);
        entry.Order.Quantity.ShouldBe(1.0009m);
        entry.Order.Reservation.ShouldBe(100.09m);
    }

    [Test]
    public void RememberedPreviewInputsDoNotAddInactiveIndicatorDependencies()
    {
        var check = new StrategyCheck(
            "OrderSizePreviewValid",
            operands: new Dictionary<string, ValueExpression>
            {
                ["amount"] = Number(value: 25m, ValueType.QuoteAmount),
                ["quantity"] = Average("Volume"),
                ["price"] = Average("Close")
            },
            side: "Buy",
            orderKind: "Market");
        var definition = new StrategyDefinition(
            [],
            [],
            [new StrategyBlock("start", "event", source: "OnStart"), new StrategyBlock("check", "condition", join: "all", checks: [check])],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        lowered.Graph.Nodes.ShouldNotContain(node => node.TypeKey == "SMA");
        TradingGraphCompilationResult compiled = new TradingGraphCompiler().Compile(lowered.Graph, Catalog);
        compiled.Validation.Diagnostics.ShouldBeEmpty();
        compiled.Graph.ShouldNotBeNull().WarmupCandles.ShouldBe(0);
    }

    private static ValueExpression Average(string field)
    {
        return new ValueExpression(
            "SMA",
            arguments: new Dictionary<string, ValueExpression>
                { ["in"] = new ValueExpression("candle", field: field), ["period"] = Number(value: 128m, ValueType.Integer) });
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