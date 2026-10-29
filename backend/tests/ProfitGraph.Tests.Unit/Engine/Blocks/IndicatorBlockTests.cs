using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Blocks;

public sealed class IndicatorBlockTests
{
    [Test]
    public void IndicatorBlocksValidateLowerAndWarmUpFromTheirPeriods()
    {
        StrategyDefinition definition = Strategy();

        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);

        foreach (string type in new[] { "EMA", "RSI", "Bollinger", "MACD" })
        {
            lowered.Graph.Nodes.ShouldContain(node => node.TypeKey == type && node.SchemaVersion == Catalog.Single(item => item.TypeKey == type).SchemaVersion);
        }

        // The MACD 3/5/2 signal needs 5 + 2 − 1 candles and its crossover one more; RSI(4) needs 5, Bollinger(4) and EMA(3) fewer.
        new TradingGraphCompiler().Compile(lowered.Graph, Catalog).Graph.ShouldNotBeNull().WarmupCandles.ShouldBe(7);
    }

    [Test]
    public void OptimizedPeriodsWarmUpForTheirLargestValue()
    {
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(
            Strategy(),
            new Dictionary<string, DecimalOptimizeParamValue>
            {
                ["slowPeriod"] = new DecimalOptimizeParamValue(Min: 5m, Max: 30m, Step: 5m),
                ["signalPeriod"] = new DecimalOptimizeParamValue(Min: 2m, Max: 9m, Step: 1m),
                ["bandWidth"] = new DecimalOptimizeParamValue(Min: 1m, Max: 3m, Step: 0.5m)
            });

        // 30 + 9 − 1 candles for the largest signal and one for the crossover.
        new TradingGraphCompiler().Compile(lowered.Graph, Catalog).Graph.ShouldNotBeNull().WarmupCandles.ShouldBe(39);
    }

    [Test]
    public void MacdCrossoverBacktestIsDeterministic()
    {
        StrategyGraph graph = new StrategyDefinitionLowerer(Catalog).Lower(Strategy()).Graph;
        BacktestResult first = Run(graph);
        BacktestResult second = Run(graph);

        first.OrderCount.ShouldBeGreaterThan(0);
        Digest(first).ShouldBe(Digest(second));
        first.Terminal.TotalPnl.ShouldBe(second.Terminal.TotalPnl);
    }

    [TestCase("fastPeriod", "5", "MACD requires fastPeriod < slowPeriod.")]
    [TestCase("fastPeriod", "6", "MACD requires fastPeriod < slowPeriod.")]
    [TestCase("bandWidth", "0", "Bollinger Bands require a positive multiplier.")]
    [TestCase("bandWidth", "-1.5", "Bollinger Bands require a positive multiplier.")]
    [TestCase("rsiPeriod", "0", "Indicator periods must be integers from 1 through 200.")]
    [TestCase("signalPeriod", "201", "Indicator periods must be integers from 1 through 200.")]
    public void InvalidIndicatorSettingsAreReported(string parameter, string value, string message)
    {
        StrategyDefinition definition = WithParameter(parameter, item => item with { DefaultValue = decimal.Parse(value, CultureInfo.InvariantCulture) });

        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.Message == message);
    }

    [Test]
    public void ConstantMacdPeriodsAreCheckedToo()
    {
        StrategyDefinition definition = WithValue("macd", Macd("macd", Constant(9m), Constant(9m)));

        new StrategyDefinitionValidator().Validate(definition, Catalog)
            .Diagnostics.ShouldContain(diagnostic => diagnostic.Message == "MACD requires fastPeriod < slowPeriod." && diagnostic.ValueName == "macd");
    }

    [TestCase(arg1: null, "Choose a value field.")]
    [TestCase("top", "Unknown value field: top.")]
    public void BandsNeedAKnownOutputField(string? field, string message)
    {
        StrategyDefinition definition = WithValue("lower", Bands(field));

        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.Message == message);
    }

    [Test]
    public void MissingAndUnknownIndicatorOperandsAreReported()
    {
        var macd = new ValueExpression(
            "MACD",
            field: "macd",
            arguments: new Dictionary<string, ValueExpression>
            {
                ["in"] = Value("close"), ["fastPeriod"] = Parameter("fastPeriod"), ["slowPeriod"] = Parameter("slowPeriod"),
                ["period"] = Parameter("signalPeriod")
            });

        IReadOnlyList<StrategyBlockDiagnostic> diagnostics = new StrategyDefinitionValidator().Validate(WithValue("macd", macd), Catalog).Diagnostics;

        diagnostics.ShouldContain(diagnostic => diagnostic.Message == "Missing value operand: signalPeriod.");
        diagnostics.ShouldContain(diagnostic => diagnostic.Message == "Unknown value operand: period.");
    }

    [Test]
    public void BandMultiplierMustBeAScalar()
    {
        StrategyDefinition definition = WithParameter("bandWidth", item => item with { ValueType = ValueType.Integer });

        new StrategyDefinitionValidator().Validate(definition, Catalog)
            .Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits);
    }

    /// <summary>
    ///     Buys when MACD crosses above its signal while RSI is below 70 and the close is above the lower band and the EMA;
    ///     sells when MACD crosses below its signal.
    /// </summary>
    private static StrategyDefinition Strategy()
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("fastPeriod", ValueType.Integer, DefaultValue: 3m), new StrategyParameter("slowPeriod", ValueType.Integer, DefaultValue: 5m),
                new StrategyParameter("signalPeriod", ValueType.Integer, DefaultValue: 2m), new StrategyParameter("rsiPeriod", ValueType.Integer, DefaultValue: 4m),
                new StrategyParameter("bandPeriod", ValueType.Integer, DefaultValue: 4m), new StrategyParameter("bandWidth", ValueType.Scalar, DefaultValue: 2m),
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, DefaultValue: 100m)
            ],
            [
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue("macd", Macd("macd", Parameter("fastPeriod"), Parameter("slowPeriod"))),
                new StrategyValue("signal", Macd("signal", Parameter("fastPeriod"), Parameter("slowPeriod"))),
                new StrategyValue(
                    "rsi",
                    new ValueExpression("RSI", arguments: new Dictionary<string, ValueExpression> { ["in"] = Value("close"), ["period"] = Parameter("rsiPeriod") })),
                new StrategyValue("lower", Bands("lower")),
                new StrategyValue(
                    "trend",
                    new ValueExpression("EMA", arguments: new Dictionary<string, ValueExpression> { ["in"] = Value("close"), ["period"] = Constant(3m) }))
            ],
            [
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock(
                    "buySignal",
                    "condition",
                    "Сигнал на покупку",
                    join: "all",
                    checks:
                    [
                        Check("CrossesAbove", Value("macd"), Value("signal")), Check("Compare", Value("rsi"), Scalar(70m), "<"),
                        Check("Compare", Value("close"), Value("lower"), ">"), Check("Compare", Value("close"), Value("trend"), ">="),
                        new StrategyCheck("NoActiveOrders")
                    ]),
                new StrategyBlock(
                    "sellSignal",
                    "condition",
                    "Сигнал на продажу",
                    join: "all",
                    checks: [Check("CrossesBelow", Value("macd"), Value("signal")), new StrategyCheck("HasBaseBalance"), new StrategyCheck("NoActiveOrders")]),
                new StrategyBlock("buy", "order", "Покупка", orderKind: "Market", side: "Buy", tag: Tag("macd.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock(
                    "sell",
                    "order",
                    "Продажа",
                    orderKind: "Market",
                    side: "Sell",
                    tag: Tag("macd.exit"),
                    quantity: new ValueExpression("balance", field: "baseAvailable"))
            ],
            [
                new StrategyWire(new StrategyExit("clock", "out"), "buySignal"), new StrategyWire(new StrategyExit("clock", "out"), "sellSignal"),
                new StrategyWire(new StrategyExit("buySignal", "yes"), "buy"), new StrategyWire(new StrategyExit("sellSignal", "yes"), "sell")
            ]);
    }

    private static StrategyDefinition WithParameter(string name, Func<StrategyParameter, StrategyParameter> change)
    {
        StrategyDefinition definition = Strategy();

        return new StrategyDefinition(
            definition.Parameters.Select(parameter => parameter.Name == name ? change(parameter) : parameter),
            definition.Values,
            definition.Blocks,
            definition.Wires);
    }

    private static StrategyDefinition WithValue(string name, ValueExpression expression)
    {
        StrategyDefinition definition = Strategy();

        return new StrategyDefinition(
            definition.Parameters,
            definition.Values.Select(value => value.Name == name ? value with { Expression = expression } : value),
            definition.Blocks,
            definition.Wires);
    }

    private static ValueExpression Macd(string field, ValueExpression fast, ValueExpression slow)
    {
        return new ValueExpression(
            "MACD",
            field: field,
            arguments: new Dictionary<string, ValueExpression>
            {
                ["in"] = Value("close"), ["fastPeriod"] = fast, ["slowPeriod"] = slow, ["signalPeriod"] = Parameter("signalPeriod")
            });
    }

    private static ValueExpression Bands(string? field)
    {
        return new ValueExpression(
            "Bollinger",
            field: field,
            arguments: new Dictionary<string, ValueExpression>
            {
                ["in"] = Value("close"), ["period"] = Parameter("bandPeriod"), ["multiplier"] = Parameter("bandWidth")
            });
    }

    private static StrategyCheck Check(string type, ValueExpression a, ValueExpression b, string? operation = null)
    {
        return new StrategyCheck(type, operation, new Dictionary<string, ValueExpression> { ["a"] = a, ["b"] = b });
    }

    private static ValueExpression Parameter(string name)
    {
        return new ValueExpression("parameter", name: name);
    }

    private static ValueExpression Value(string name)
    {
        return new ValueExpression("value", name: name);
    }

    private static ValueExpression Constant(decimal value)
    {
        return new ValueExpression("constant", ValueType.Integer, value);
    }

    private static ValueExpression Tag(string tag)
    {
        return new ValueExpression("constant", ValueType.Tag, text: tag);
    }

    private static ValueExpression Scalar(decimal value)
    {
        return new ValueExpression("constant", ValueType.Scalar, value);
    }

    private static string Digest(BacktestResult result)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result.Ledger))));
    }

    private static BacktestResult Run(StrategyGraph graph)
    {
        TradingGraphCompilationResult compiled = new TradingGraphCompiler().Compile(graph, Catalog);
        compiled.Validation.Diagnostics.ShouldBeEmpty();
        decimal[] prices = Enumerable.Range(start: 0, count: 80).Select(index => 100m + (index % 12 < 6 ? index % 12 : 12 - index % 12) * 1.7m + index * 0.1m).ToArray();
        var input = new BacktestInput(
            compiled.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            prices.Select((price, index) => EngineGraphFixture.Candle(index, price)));

        return new BacktestEngine().Run(input);
    }
}