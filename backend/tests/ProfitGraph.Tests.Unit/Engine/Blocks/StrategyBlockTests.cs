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
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Blocks;

public sealed class StrategyBlockTests
{
    [Test]
    public void AtrWithIntegerParameterLowersAndValidates()
    {
        StrategyDefinition definition = AtrLadderStrategyDefinitionGenerator.Generate();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        lowered.Graph.Nodes.Single(node => node.TypeKey == "ATR").SchemaVersion.ShouldBe(Catalog.Single(item => item.TypeKey == "ATR").SchemaVersion);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LoweredTemplatesKeepFrozenExactReplay(bool ladder)
    {
        StrategyDefinition definition = Template(ladder);
        definition.Blocks.Count.ShouldBe(ladder ? 13 : 5);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        lowered.Sources.Count.ShouldBe(lowered.Graph.Nodes.Count);
        BacktestResult actual = Run(lowered.Graph);
        actual.OrderCount.ShouldBe(ladder ? 15 : 2);
        actual.FillCount.ShouldBe(ladder ? 9 : 2);
        actual.Terminal.TotalPnl.ShouldBe(ladder ? -204.271082910m : -83.333330m);

        // Captured only after the complete ledger matched the original factories, without normalization.
        Digest(actual)
            .ShouldBe(
                ladder
                    ? "22A5A0CEFD6F54ED3973EAF0AA9E3E1CCA2790E1FC27FC7816D151E87D91E425"
                    : "85F16AD8899F8BBAF3E2A1C17C13D03F78C48DC0D590E682F8C37BBE317CD0D3");
    }

    [Test]
    public void InvalidChecksMapUnitErrorsToVisibleBlock()
    {
        StrategyDefinition template = Template(ladder: false);
        var condition = new StrategyBlock(
            "bad",
            "condition",
            join: "all",
            checks:
            [
                new StrategyCheck(
                    "Compare",
                    ">",
                    new Dictionary<string, ValueExpression>
                    {
                        ["a"] = new ValueExpression("constant", ValueType.Price, number: 1m), ["b"] = new ValueExpression("constant", ValueType.BaseAmount, number: 1m)
                    })
            ]);
        var definition = new StrategyDefinition(
            template.Parameters,
            template.Values,
            template.Blocks.Append(condition),
            template.Wires.Append(new StrategyWire(new StrategyExit("clock", "out"), "bad")));
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits && diagnostic.BlockId == "bad");
        result.Diagnostics.ShouldAllBe(diagnostic => !diagnostic.Message.Contains('$'));
    }

    [Test]
    public void UnknownReferencesAndCyclesAreDiagnosticsRatherThanLoweringFailures()
    {
        var definition = new StrategyDefinition(
            [],
            [new StrategyValue("a", new ValueExpression("value", name: "b")), new StrategyValue("b", new ValueExpression("value", name: "a"))],
            [
                new StrategyBlock("event", "event", source: "OnStart"), new StrategyBlock(
                    "condition",
                    "condition",
                    join: "all",
                    checks:
                    [new StrategyCheck("Compare", "=", new Dictionary<string, ValueExpression> { ["a"] = new ValueExpression("parameter", name: "missing") })])
            ],
            [new StrategyWire(new StrategyExit("event", "missing"), "condition")]);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.Cycle && diagnostic.ValueName == "a");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownParameter && diagnostic.BlockId == "condition");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.MissingInput && diagnostic.BlockId == "condition");
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownPort && diagnostic.BlockId == "event");
    }

    [Test]
    public void BadIndicatorPeriodPointsToItsNamedParameter()
    {
        StrategyDefinition definition = SmaStrategyBlockFactory.Create(fastPeriod: 0);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.ParameterName == "fastPeriod");
    }

    [Test]
    public void LoweringRetainsSharedValuesAndNamedOptimizationIdentity()
    {
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(Template(ladder: false));
        lowered.Graph.Nodes.Count(node => node.TypeKey == "SMA").ShouldBe(2);
        lowered.Graph.Nodes.Single(node => node.Id == StrategyDefinitionLowerer.ParameterNodeId("fastPeriod"))
            .ParameterReference("value")
            .NodeId.ShouldBe("fastPeriod");
        lowered.Graph.Nodes.Single(node => node.Id == "buy").Id.ShouldBe("buy");
    }

    [Test]
    public void ActiveOrderAcceptsNamedTagExpressionAndTypedConstants()
    {
        var definition = new StrategyDefinition(
            [],
            [
                new StrategyValue("tag", new ValueExpression("constant", ValueType.Tag, text: "entry")),
                new StrategyValue("literal", new ValueExpression("constant", ValueType.Boolean, text: "false")),
                new StrategyValue(
                    "price",
                    new ValueExpression(
                        "activeOrder",
                        field: "price",
                        arguments:
                        new Dictionary<string, ValueExpression> { ["tag"] = new ValueExpression("value", name: "tag") }))
            ],
            [
                new StrategyBlock("start", "event", source: "OnStart"), new StrategyBlock(
                    "check",
                    "condition",
                    join: "all",
                    checks:
                    [
                        new StrategyCheck(
                            "Compare",
                            "=",
                            new Dictionary<string, ValueExpression>
                                { ["a"] = new ValueExpression("value", name: "price"), ["b"] = new ValueExpression("constant", ValueType.Price, number: 100m) })
                    ])
            ],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        lowered.Graph.Nodes.Single(node => node.TypeKey == "ActiveOrder").Parameters.ShouldContainKey("tag");
        new TradingGraphCompiler().Compile(lowered.Graph, Catalog).Validation.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void CompositeDiagnosticsHidePrimitiveInnerNames()
    {
        var definition = new StrategyDefinition(
            [],
            [],
            [
                new StrategyBlock("start", "event", source: "OnStart"), new StrategyBlock(
                    "check",
                    "condition",
                    join: "all",
                    checks:
                    [
                        new StrategyCheck(
                            "HasBaseBalance",
                            operands: new Dictionary<string, ValueExpression>
                                { ["minQuantity"] = new ValueExpression("constant", ValueType.Tag, text: "invalid") })
                    ])
            ],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.IsValid.ShouldBeFalse();
        result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.BlockId == "check" && !diagnostic.Message.Contains("zeroBase", StringComparison.Ordinal) &&
            !diagnostic.Message.Contains("Внутри узла", StringComparison.Ordinal) && !diagnostic.Message.Contains('$'));
    }

    [TestCase("valid")]
    [TestCase("out")]
    public void PositionFlatPreviewSelectsItsPublishedOutput(string output)
    {
        var definition = new StrategyDefinition(
            [],
            [],
            [
                new StrategyBlock("start", "event", source: "OnStart"), new StrategyBlock(
                    "check",
                    "condition",
                    join: "all",
                    checks:
                    [
                        new StrategyCheck(
                            "PositionFlatPreview",
                            output,
                            new Dictionary<string, ValueExpression>
                                { ["quantity"] = new ValueExpression("constant", ValueType.BaseAmount, number: 1m) })
                    ])
            ],
            [new StrategyWire(new StrategyExit("start", "out"), "check")]);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        lowered.Graph.Connections.Single(connection => connection.Target == new PortReference("check", "condition"))
            .Source.PortKey.ShouldBe(output);
    }

    [TestCase("Boolean", "not-boolean")]
    [TestCase("RejectionReason", "missing")]
    [TestCase("OrderSide", "Buy")]
    public void UnsupportedConstantLiteralsReturnValueDiagnostics(string type, string text)
    {
        var definition = new StrategyDefinition(
            [],
            [new StrategyValue("bad", new ValueExpression("constant", Enum.Parse<ValueType>(type), text: text))],
            [new StrategyBlock("start", "event", source: "OnStart")],
            []);
        new StrategyDefinitionValidator().Validate(definition, Catalog).Diagnostics.ShouldContain(diagnostic => diagnostic.ValueName == "bad");
    }

    [TestCase("candle")]
    [TestCase("balance")]
    [TestCase("position")]
    [TestCase("activeOrder")]
    public void UnusedNamedValuesStillValidateTheirSelectedField(string kind)
    {
        var definition = new StrategyDefinition(
            [],
            [new StrategyValue("unused", new ValueExpression(kind, text: kind == "activeOrder" ? "entry" : null, field: "missing"))],
            [new StrategyBlock("start", "event", source: "OnStart")],
            []);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.IsValid.ShouldBeFalse();
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownPort && diagnostic.ValueName == "unused");
    }

    [TestCase("parameter")]
    [TestCase("value")]
    [TestCase("constant")]
    [TestCase("candle")]
    public void UnsupportedExpressionArgumentsReturnDiagnosticsInsteadOfBeingDiscarded(string kind)
    {
        var expression = new ValueExpression(
            kind,
            ValueType.Scalar,
            number: 1m,
            name: "known",
            field: "Close",
            arguments:
            new Dictionary<string, ValueExpression> { ["unexpected"] = new ValueExpression("constant", ValueType.Scalar, number: 1m) });
        var definition = new StrategyDefinition(
            [new StrategyParameter("known", ValueType.Scalar, DefaultValue: 1m)],
            [new StrategyValue("known", new ValueExpression("constant", ValueType.Scalar, number: 1m)), new StrategyValue("unused", expression)],
            [new StrategyBlock("start", "event", source: "OnStart")],
            []);
        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, Catalog);
        result.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownParameter && diagnostic.ValueName == "unused");
    }

    private static StrategyDefinition Template(bool ladder)
    {
        return ladder ? LadderStrategyBlockFactory.Create() : SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 3);
    }

    private static string Digest(BacktestResult result)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result.Ledger))));
    }

    private static BacktestResult Run(StrategyGraph graph)
    {
        TradingGraphCompilationResult compiled = new TradingGraphCompiler().Compile(graph, Catalog);
        compiled.Validation.Diagnostics.ShouldBeEmpty();
        decimal[] prices = [4m, 3m, 2m, 5m, 6m, 2m, 1m];
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